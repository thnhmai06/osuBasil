using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Configurations;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Beatmaps;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
	private Beatmapset? _mapset;

	public BeatmapsetManagementEndpointTests(WebApplicationFactory<Program> factory)
	{
		var mapsets = Substitute.For<IBeatmapsetRepository>();
		mapsets.FetchByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(call => _mapset?.Id == call.ArgAt<int>(0) ? _mapset : null);
		mapsets.FetchAllIdsAsync(Arg.Any<CancellationToken>())
			.Returns(_ => (IReadOnlyList<int>)(_mapset is not null ? [_mapset.Id] : []));
		mapsets.FetchPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(_ => (IReadOnlyList<Beatmapset>)(_mapset is not null ? [_mapset] : []));
		mapsets.WhenForAnyArgs(m => m.SetFrozenAsync(default, default))
			.Do(call =>
			{
				if (_mapset?.Id == call.ArgAt<int>(0)) _mapset = _mapset with { IsFrozen = call.ArgAt<bool>(1) };
			});
		mapsets.WhenForAnyArgs(m => m.SetPrivateAsync(default, default))
			.Do(call =>
			{
				if (_mapset?.Id == call.ArgAt<int>(0)) _mapset = _mapset with { IsPrivate = call.ArgAt<bool>(1) };
			});
		mapsets.FetchCountAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(call => _mapset is not null && (call.ArgAt<bool>(0) || !_mapset.IsPrivate) ? 1 : 0);

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
				services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions { Path = "" }));
				services.AddSingleton(TestDoubles.FixedAdminKeySettingsRepository(AdminKey));
				services.AddSingleton(mapsets);
				services.AddSingleton(TestDoubles.NullMapRepository());
				services.AddSingleton(Options.Create(new StorageOptions
				{
					ReplaysPath = Path.Combine(_dataDir, "Replays"),
					AvatarsPath = Path.Combine(_dataDir, "Avatars"),
					MapsetsPath = Path.Combine(_dataDir, "Mapsets"),
					SeasonalsPath = Path.Combine(_dataDir, "Seasonals"),
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

	private string MapsetFolder(int setId)
	{
		var folder = Path.Combine(_dataDir, "Mapsets", $"{setId} Artist - Title");
		Directory.CreateDirectory(folder);
		return folder;
	}

	private static async Task<byte[]> MakeMinimalOszAsync()
	{
		using var stream = new MemoryStream();
		using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
		{
			var entry = archive.CreateEntry("replacement.osu");
			await using var entryStream = await entry.OpenAsync();
			await entryStream.WriteAsync("osu file format v14"u8.ToArray());
		}

		return stream.ToArray();
	}

	// ---- PUT /beatmapsets/{mapsetId} ----

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
		_mapset = new Beatmapset(700, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow, true);
		MapsetFolder(700);

		var request = MakeRequest(HttpMethod.Put, "/beatmapsets/700");
		request.Content = new MultipartFormDataContent
			{ { new ByteArrayContent(await MakeMinimalOszAsync()), "file", "set.osz" } };

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
	}

	[Fact]
	public async Task PutBeatmapset_Valid_ExtractsIntoResolvedFolderAndReturns202()
	{
		_mapset = new Beatmapset(701, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);
		var folder = MapsetFolder(701);
		await File.WriteAllTextAsync(Path.Combine(folder, "old.osu"), "stale content");

		var request = MakeRequest(HttpMethod.Put, "/beatmapsets/701");
		request.Content = new MultipartFormDataContent
			{ { new ByteArrayContent(await MakeMinimalOszAsync()), "file", "set.osz" } };

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
		Assert.True(File.Exists(Path.Combine(folder, "replacement.osu")));
		Assert.True(File.Exists(Path.Combine(folder, "old.osu")));
	}

	// ---- DELETE /beatmapsets/{mapsetId} ----

	[Fact]
	public async Task DeleteBeatmapset_UnknownId_ReturnsNotFound()
	{
		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/beatmapsets/999999"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task DeleteBeatmapset_Frozen_ReturnsConflict_FolderUntouched()
	{
		_mapset = new Beatmapset(800, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow, true);
		var folder = MapsetFolder(800);

		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/beatmapsets/800"));

		Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
		Assert.True(Directory.Exists(folder));
	}

	[Fact]
	public async Task DeleteBeatmapset_Valid_RenamesToDeletedMarkerAndReturns202()
	{
		_mapset = new Beatmapset(801, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);
		var folder = MapsetFolder(801);

		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/beatmapsets/801"));

		Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
		Assert.False(Directory.Exists(folder));
		var renamed = Directory.EnumerateDirectories(Path.Combine(_dataDir, "Mapsets"))
			.FirstOrDefault(d => d.Contains(BeatmapIngestionService.DeletedFolderInfix));
		Assert.NotNull(renamed);
	}

	// ---- PATCH /beatmapsets/{mapsetId} ----

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
		_mapset = new Beatmapset(900, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);

		var setRequest = MakeRequest(HttpMethod.Patch, "/beatmapsets/900");
		setRequest.Content = JsonContent.Create(new { frozen = true, @private = true });
		var setResponse = await _factory.CreateClient().SendAsync(setRequest);

		Assert.True(setResponse.IsSuccessStatusCode);
		Assert.True(_mapset!.IsFrozen);
		Assert.True(_mapset!.IsPrivate);

		var clearRequest = MakeRequest(HttpMethod.Patch, "/beatmapsets/900");
		clearRequest.Content = JsonContent.Create(new { frozen = false, @private = false });
		await _factory.CreateClient().SendAsync(clearRequest);

		Assert.False(_mapset!.IsFrozen);
		Assert.False(_mapset!.IsPrivate);
	}

	[Fact]
	public async Task PatchBeatmapset_OmittedField_LeavesItUnchanged()
	{
		_mapset = new Beatmapset(902, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow,
			IsPrivate: true);

		var request = MakeRequest(HttpMethod.Patch, "/beatmapsets/902");
		request.Content = JsonContent.Create(new { frozen = true });
		await _factory.CreateClient().SendAsync(request);

		Assert.True(_mapset!.IsFrozen);
		Assert.True(_mapset!.IsPrivate);
	}

	[Fact]
	public async Task PatchBeatmapset_MissingAdminKey_ReturnsUnauthorized()
	{
		_mapset = new Beatmapset(901, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);

		var request = MakeRequest(HttpMethod.Patch, "/beatmapsets/901", null);
		request.Content = JsonContent.Create(new { frozen = true });

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}
}