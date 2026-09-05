using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Configurations;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Beatmaps;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers the admin-key-gated `/beatmapsets` write routes: `PUT`/`DELETE` (both filesystem-first
///     and asynchronous — 202, never a synchronous DB touch) and `PATCH` (the combined frozen/private
///     write-lock those two respect — frozen blocks `PUT`/`DELETE`, private hides the beatmapset and every
///     beatmap under it from non-admin reads). A stub `IBeatmapsetRepository` stands in for the database
///     (this suite is about the route/filesystem behavior, not persistence), while the beatmapset's
///     storage folder is a real temp directory so `Directory.Move`/zip-extraction actually run.
/// </summary>
public class BeatmapsetManagementEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
	private const string AdminKey = "correct-key";
	private readonly string _dataDir = Directory.CreateTempSubdirectory("basil-beatmapset-mgmt-tests-").FullName;
	private readonly WebApplicationFactory<Program> _factory;
	private readonly IBeatmapsetRepository _beatmapsets;
	private Beatmapset? _beatmapset;

	public BeatmapsetManagementEndpointTests(WebApplicationFactory<Program> factory)
	{
		var beatmapsets = _beatmapsets = Substitute.For<IBeatmapsetRepository>();
		beatmapsets.FetchByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(call => _beatmapset?.Id == call.ArgAt<int>(0) ? _beatmapset : null);
		beatmapsets.FetchAllIdsAsync(Arg.Any<CancellationToken>())
			.Returns(_ => (IReadOnlyList<int>)(_beatmapset is not null ? [_beatmapset.Id] : []));
		beatmapsets.FetchPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(_ => (IReadOnlyList<Beatmapset>)(_beatmapset is not null ? [_beatmapset] : []));
		beatmapsets.WhenForAnyArgs(m => m.SetFrozenAsync(default, default))
			.Do(call =>
			{
				if (_beatmapset?.Id == call.ArgAt<int>(0))
					_beatmapset = _beatmapset with { IsFrozen = call.ArgAt<bool>(1) };
			});
		beatmapsets.WhenForAnyArgs(m => m.SetPrivateAsync(default, default))
			.Do(call =>
			{
				if (_beatmapset?.Id == call.ArgAt<int>(0))
					_beatmapset = _beatmapset with { IsPrivate = call.ArgAt<bool>(1) };
			});
		beatmapsets.FetchCountAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(call => _beatmapset is not null && (call.ArgAt<bool>(0) || !_beatmapset.IsPrivate) ? 1 : 0);
		// Unstubbed NSubstitute methods return null for a reference type -- POST /beatmapsets'
		// ingestion path resolves/creates a Beatmapset via UpsertAsync and needs a real instance back,
		// or BeatmapIngestionService.BeatmapsetFolderName throws a NullReferenceException on it.
		beatmapsets.UpsertAsync(Arg.Any<Beatmapset>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				_beatmapset = call.ArgAt<Beatmapset>(0);
				return Task.FromResult(_beatmapset);
			});

		_factory = factory.WithWebHostBuilder(builder =>
		{
			builder.ConfigureAppConfiguration((_, config) =>
			{
				config.AddInMemoryCollection(new Dictionary<string, string?>
				{
					["Basil:Server:Domain"] = "test.local",
					["Basil:Bot:CommandPrefix"] = "!"
				});
			});
			builder.ConfigureServices(services =>
			{
				services.AddSingleton(Options.Create(new DatabaseOptions { Path = "" }));
				services.AddSingleton(TestDoubles.FixedAdminKeySettingsRepository());
				services.AddSingleton(beatmapsets);
				services.AddSingleton(TestDoubles.NullMapRepository());
				services.AddSingleton(Options.Create(new StorageOptions
				{
					ReplaysPath = Path.Combine(_dataDir, "Replays"),
					AvatarsPath = Path.Combine(_dataDir, "Avatars"),
					BeatmapsetsPath = Path.Combine(_dataDir, "Beatmapsets"),
					MenuSeasonalsPath = Path.Combine(_dataDir, "Seasonals"),
					MenuBannersPath = Path.Combine(_dataDir, "Banners"),
					FaqsPath = Path.Combine(_dataDir, "Faqs"),
					CachePath = Path.Combine(_dataDir, "Cache")
				}));
			});
		});
	}

	public void Dispose()
	{
		if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, true);
	}

	private static HttpRequestMessage MakeRequest(HttpMethod method, string path, string? adminKey = AdminKey)
	{
		var request = new HttpRequestMessage(method, path) { Headers = { Host = "api.test.local" } };
		if (adminKey is not null) request.Headers.Add("Authorization", $"Bearer {adminKey}");
		return request;
	}

	private string BeatmapsetFolder(int setId)
	{
		var folder = Path.Combine(_dataDir, "Beatmapsets", $"{setId} Artist - Title");
		Directory.CreateDirectory(folder);
		return folder;
	}

	/// <summary>
	///     Builds a variant of <see cref="_factory" /> with <see cref="BeatmapWatcherService" /> and
	///     <see cref="BeatmapsetMigrationService" /> both removed, so a seeded legacy folder is
	///     guaranteed to still be a legacy folder -- and reconciled by nothing but the route itself --
	///     when the request under test runs. Only used by the two tests that specifically pin
	///     Phase 7's "legacy branch reconciles inline" contract; every other test in this class keeps
	///     the real (racy) background services, matching production.
	/// </summary>
	private WebApplicationFactory<Program> FactoryWithoutBackgroundBeatmapServices()
	{
		return _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
		{
			RemoveHostedService<BeatmapWatcherService>(services);
			RemoveHostedService<BeatmapsetMigrationService>(services);
		}));
	}

	private static void RemoveHostedService<T>(IServiceCollection services) where T : class
	{
		var descriptor = services.FirstOrDefault(d =>
			d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(T));
		if (descriptor is not null) services.Remove(descriptor);
	}

	private static async Task<byte[]> MakeMinimalOszAsync()
	{
		using var stream = new MemoryStream();
		await using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
		{
			var entry = archive.CreateEntry("replacement.osu");
			await using var entryStream = await entry.OpenAsync();
			await entryStream.WriteAsync("osu file format v14"u8.ToArray());
		}

		return stream.ToArray();
	}

	/// <summary>
	///     Unlike <see cref="MakeMinimalOszAsync" />, this ".osu" content actually decodes -- ingestion
	///     (`BeatmapIngestionService.ReconcileOszAsync`) silently skips a ".osu" entry it can't decode
	///     (`TryDecode`'s catch-all), so a route that needs to observe a real ingested beatmap count
	///     needs a genuinely parseable file, not just any bytes ending in ".osu".
	/// </summary>
	private static async Task<byte[]> MakeDecodableOszAsync(string title)
	{
		var content = $"""
		               osu file format v14

		               [General]
		               AudioFilename: audio.mp3

		               [Metadata]
		               Title:{title}
		               Artist:Test Artist
		               Creator:Test Creator
		               Version:Normal

		               [Difficulty]
		               HPDrainRate:5
		               CircleSize:5
		               OverallDifficulty:5
		               ApproachRate:5
		               SliderMultiplier:1.4
		               SliderTickRate:1

		               [TimingPoints]
		               0,500,4,2,0,100,1,0

		               [HitObjects]
		               256,192,0,1,0,0:0:0:0:
		               """;

		using var stream = new MemoryStream();
		await using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
		{
			var entry = archive.CreateEntry("beatmap.osu");
			await using var entryStream = await entry.OpenAsync();
			await entryStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(content));
		}

		return stream.ToArray();
	}

	// ---- POST /beatmapsets ----

	/// <summary>
	///     Pins that upload reconciles only the uploaded archive (`BeatmapIngestionService.ReconcileOszAsync`),
	///     not a full-directory sweep (`ReconcileAllAsync`) -- a stray, unrelated `.osz` already sitting in
	///     `BeatmapsetsPath` (simulating one the watcher hasn't picked up yet) is left exactly as-is by an
	///     unrelated upload.
	/// </summary>
	[Fact]
	public async Task PostBeatmapset_Valid_ReconcilesOnlyTheUpload_LeavesUnrelatedStrayOszUntouched()
	{
		var beatmapsetsDir = Path.Combine(_dataDir, "Beatmapsets");
		Directory.CreateDirectory(beatmapsetsDir);
		var strayOszPath = Path.Combine(beatmapsetsDir, "stray.osz");
		await File.WriteAllBytesAsync(strayOszPath, await MakeMinimalOszAsync());

		var request = MakeRequest(HttpMethod.Post, "/beatmapsets");
		request.Content = new MultipartFormDataContent
			{ { new ByteArrayContent(await MakeDecodableOszAsync("Uploaded Set")), "file", "set.osz" } };

		var response = await _factory.CreateClient().SendAsync(request);
		var body = await response.Content.ReadFromJsonAsync<JsonElement>();

		Assert.Equal(HttpStatusCode.Created, response.StatusCode);
		Assert.Equal(1, body.GetProperty("data").GetProperty("beatmapsProcessed").GetInt32());
		Assert.True(File.Exists(strayOszPath));
	}

	[Fact]
	public async Task PostBeatmapset_MissingFile_ReturnsBadRequest()
	{
		var request = MakeRequest(HttpMethod.Post, "/beatmapsets");
		request.Content = new MultipartFormDataContent { { new StringContent("no file here"), "note" } };

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	/// <summary>
	///     Regression test: a truly empty <see cref="MultipartFormDataContent" /> (no parts at all) isn't
	///     a well-formed multipart body ASP.NET Core's form parser accepts -- it throws
	///     <see cref="InvalidDataException" /> from inside `ReadFormAsync`, which `HandleCreate` didn't
	///     catch, surfacing as an unhandled 500 instead of a 400. Found via this exact test; fixed by
	///     catching it alongside the missing-file/wrong-extension checks.
	/// </summary>
	[Fact]
	public async Task PostBeatmapset_MalformedMultipart_ReturnsBadRequest()
	{
		var request = MakeRequest(HttpMethod.Post, "/beatmapsets");
		request.Content = new MultipartFormDataContent();

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	// ---- PUT /beatmapsets/{beatmapsetId} ----

	[Fact]
	public async Task PutBeatmapset_UnknownId_ReturnsNotFound()
	{
		var request = MakeRequest(HttpMethod.Put, "/beatmapsets/999999");
		request.Content = new MultipartFormDataContent
			{ { new ByteArrayContent(await MakeMinimalOszAsync()), "file", "set.osz" } };

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task PutBeatmapset_Frozen_ReturnsConflict()
	{
		_beatmapset = new Beatmapset(700, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow, true);
		BeatmapsetFolder(700);

		var request = MakeRequest(HttpMethod.Put, "/beatmapsets/700");
		request.Content = new MultipartFormDataContent
			{ { new ByteArrayContent(await MakeMinimalOszAsync()), "file", "set.osz" } };

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
	}

	/// <summary>Same malformed-multipart guard as `POST /beatmapsets`; `HandleReplace` has the identical gap.</summary>
	[Fact]
	public async Task PutBeatmapset_MalformedMultipart_ReturnsBadRequest()
	{
		_beatmapset = new Beatmapset(702, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);
		BeatmapsetFolder(702);

		var request = MakeRequest(HttpMethod.Put, "/beatmapsets/702");
		request.Content = new MultipartFormDataContent();

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	/// <summary>
	///     Covers the observable contract (202 Accepted, the new archive's content is what's actually
	///     served), not which of the two storage layouts backed it: see
	///     <see cref="DeleteBeatmapset_Valid_RemovesTheBeatmapsetsLocalFilesAndReturns202" />'s remarks
	///     on why a folder seeded before the shared host's first request may already be migrated to the
	///     canonical ".osz" layout by the time this PUT request runs. On that layout, HandleReplace does
	///     a clean swap (old content invalidated, not merged with the new archive) rather than the
	///     legacy folder path's overlay-extract, so a stale file that predates the upload is not
	///     expected to survive here the way it would for an un-migrated folder.
	/// </summary>
	[Fact]
	public async Task PutBeatmapset_Valid_ReplacesTheBeatmapsetsFilesAndReturns202()
	{
		_beatmapset = new Beatmapset(701, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);
		var folder = BeatmapsetFolder(701);
		await File.WriteAllTextAsync(Path.Combine(folder, "old.osu"), "stale content");

		var request = MakeRequest(HttpMethod.Put, "/beatmapsets/701");
		request.Content = new MultipartFormDataContent
			{ { new ByteArrayContent(await MakeMinimalOszAsync()), "file", "set.osz" } };

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
		var beatmapsetsPath = Path.Combine(_dataDir, "Beatmapsets");
		var canonicalOsz = Directory.EnumerateFiles(beatmapsetsPath, "701 *.osz").SingleOrDefault();
		Assert.NotNull(canonicalOsz);
		await using var archive = await ZipFile.OpenReadAsync(canonicalOsz);
		Assert.Contains(archive.Entries, e => e.Name == "replacement.osu");
	}

	/// <summary>
	///     Phase 7 regression: the legacy-folder branch of <c>HandleReplace</c> used to rely entirely on
	///     the live <see cref="BeatmapWatcherService" /> noticing the extracted files and reconciling
	///     them -- with the watcher (and <see cref="BeatmapsetMigrationService" />) removed from this
	///     request's host entirely, that can no longer happen, so the DB row must appear from the route
	///     handler's own inline <c>ReconcileFolderAsync</c> call, immediately, with no polling.
	/// </summary>
	[Fact]
	public async Task PutBeatmapset_LegacyFolderWithNoBackgroundServicesRunning_ReconcilesInline()
	{
		_beatmapset = new Beatmapset(703, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);
		BeatmapsetFolder(703);

		var beatmaps = Substitute.For<IBeatmapRepository>();
		beatmaps.FetchAllBySetIdAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns((IReadOnlyList<Beatmap>)[]);

		var factory = FactoryWithoutBackgroundBeatmapServices()
			.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton(beatmaps)));

		var request = MakeRequest(HttpMethod.Put, "/beatmapsets/703");
		request.Content = new MultipartFormDataContent
			{ { new ByteArrayContent(await MakeDecodableOszAsync("Replacement")), "file", "set.osz" } };

		var response = await factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
		await beatmaps.Received(1).UpsertAsync(Arg.Any<Beatmap>(), Arg.Any<CancellationToken>());
	}

	// ---- DELETE /beatmapsets/{beatmapsetId} ----

	[Fact]
	public async Task DeleteBeatmapset_UnknownId_ReturnsNotFound()
	{
		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/beatmapsets/999999"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	/// <summary>
	///     The frozen check rejects the request before either of <c>HandleDelete</c>'s storage-layout
	///     branches run, so the beatmapset's local files must survive regardless of which layout
	///     backed it by the time this request ran -- see
	///     <see cref="DeleteBeatmapset_Valid_RemovesTheBeatmapsetsLocalFilesAndReturns202" />'s remarks
	///     on why a folder seeded before the shared host's first request may already be migrated to the
	///     canonical ".osz" layout.
	/// </summary>
	[Fact]
	public async Task DeleteBeatmapset_Frozen_ReturnsConflict_LocalFilesUntouched()
	{
		_beatmapset = new Beatmapset(800, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow, true);
		var folder = BeatmapsetFolder(800);

		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/beatmapsets/800"));

		Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
		var beatmapsetsPath = Path.Combine(_dataDir, "Beatmapsets");
		var stillHasCanonicalOsz = Directory.EnumerateFiles(beatmapsetsPath, "800 *.osz").Any();
		Assert.True(Directory.Exists(folder) || stillHasCanonicalOsz,
			"Beatmapset's local files were removed despite the frozen check rejecting the delete.");
	}

	/// <summary>
	///     Covers the observable contract (202 Accepted, the beatmapset's on-disk files are gone), not
	///     which of the two storage layouts backed it: the shared <c>WebApplicationFactory</c> host runs
	///     <see cref="BeatmapsetMigrationService" /> as a hosted service, which converts any legacy
	///     folder it finds to the canonical ".osz" layout the moment the host starts (on this test's own
	///     first HTTP call) -- so a folder seeded before that first call is not guaranteed to still be a
	///     folder by the time this DELETE request runs. Both of <c>HandleDelete</c>'s branches (rename a
	///     still-legacy folder to the deleted-marker convention, then reconcile it inline; delete a
	///     canonical ".osz" directly) leave no local files behind either way.
	/// </summary>
	[Fact]
	public async Task DeleteBeatmapset_Valid_RemovesTheBeatmapsetsLocalFilesAndReturns202()
	{
		_beatmapset = new Beatmapset(801, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);
		var folder = BeatmapsetFolder(801);

		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/beatmapsets/801"));

		Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
		Assert.False(Directory.Exists(folder));
		var beatmapsetsPath = Path.Combine(_dataDir, "Beatmapsets");
		Assert.DoesNotContain(Directory.EnumerateFiles(beatmapsetsPath, "*.osz"),
			f => Path.GetFileName(f).StartsWith("801 "));
	}

	/// <summary>
	///     Phase 7 regression: the legacy-folder branch of <c>HandleDelete</c> used to rely entirely on
	///     the live <see cref="BeatmapWatcherService" /> noticing the rename-to-deleted-marker and
	///     reconciling the row's removal -- with the watcher (and <see cref="BeatmapsetMigrationService" />)
	///     removed from this request's host entirely, that can no longer happen, so the repository's
	///     delete must come from the route handler's own inline <c>ReconcileDeletedFolderAsync</c> call,
	///     immediately, with no polling. True regardless of which storage layout backed the set (both
	///     branches end in the same <c>DeleteBeatmapsetAsync</c> call), which is exactly why this
	///     assertion doesn't need to fight the migration race the way the on-disk assertion above does.
	/// </summary>
	[Fact]
	public async Task DeleteBeatmapset_WithNoBackgroundServicesRunning_RemovesDbRowInline()
	{
		_beatmapset = new Beatmapset(802, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);
		BeatmapsetFolder(802);

		var factory = FactoryWithoutBackgroundBeatmapServices();
		var response = await factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/beatmapsets/802"));

		Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
		await _beatmapsets.Received(1).DeleteAsync(802, Arg.Any<CancellationToken>());
	}

	// ---- PATCH /beatmapsets/{beatmapsetId} ----

	[Fact]
	public async Task PatchBeatmapset_UnknownId_ReturnsNotFound()
	{
		var request = MakeRequest(HttpMethod.Patch, "/beatmapsets/999999");
		request.Content = JsonContent.Create(new { frozen = true });

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task PatchBeatmapset_TogglesFrozenAndPrivateTogether()
	{
		_beatmapset = new Beatmapset(900, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);

		var setRequest = MakeRequest(HttpMethod.Patch, "/beatmapsets/900");
		setRequest.Content = JsonContent.Create(new { frozen = true, @private = true });
		var setResponse = await _factory.CreateClient().SendAsync(setRequest);

		Assert.True(setResponse.IsSuccessStatusCode);
		Assert.True(_beatmapset!.IsFrozen);
		Assert.True(_beatmapset!.IsPrivate);

		var clearRequest = MakeRequest(HttpMethod.Patch, "/beatmapsets/900");
		clearRequest.Content = JsonContent.Create(new { frozen = false, @private = false });
		await _factory.CreateClient().SendAsync(clearRequest);

		Assert.False(_beatmapset!.IsFrozen);
		Assert.False(_beatmapset!.IsPrivate);
	}

	[Fact]
	public async Task PatchBeatmapset_OmittedField_LeavesItUnchanged()
	{
		_beatmapset = new Beatmapset(902, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow,
			IsPrivate: true);

		var request = MakeRequest(HttpMethod.Patch, "/beatmapsets/902");
		request.Content = JsonContent.Create(new { frozen = true });
		await _factory.CreateClient().SendAsync(request);

		Assert.True(_beatmapset!.IsFrozen);
		Assert.True(_beatmapset!.IsPrivate);
	}

	[Fact]
	public async Task PatchBeatmapset_MissingAdminKey_ReturnsUnauthorized()
	{
		_beatmapset = new Beatmapset(901, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);

		var request = MakeRequest(HttpMethod.Patch, "/beatmapsets/901", null);
		request.Content = JsonContent.Create(new { frozen = true });

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}
}