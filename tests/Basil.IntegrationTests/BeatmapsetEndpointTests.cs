using System.IO.Compression;
using System.Net;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Configurations;
using Basil.Domain.Beatmaps;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers the public `/beatmapsets` routes (info + downloads): `GET /beatmapsets/{mapsetId}`
///     (which embeds each beatmap's id/version/mode inline), `GET
///     /beatmapsets/{mapsetId}/{beatmapId}` (a single difficulty's JSON metadata), and `GET
///     /beatmapsets/{mapsetId}/{beatmapId}/download` (the raw `.osu` file). Also covers MIME-type
///     correctness across every download route (osu!'s real per-extension types). File-download
///     routes (download/video/background/storyboard) 302-redirect on the `api.` host — this suite
///     verifies the redirect for one representative route each and otherwise targets the `assets.`
///     host directly, where the actual private-check/mirror-fallback/file-serving logic now lives.
/// </summary>
public class BeatmapsetEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
	// ---- GET /beatmapsets/{mapsetId}/{beatmapId}/video ----

	private const string OsuFileWithVideo = """
	                                        osu file format v14
	                                        [Events]
	                                        Video,0,"video.mp4"
	                                        """;

	private readonly string _dataDir = Directory.CreateTempSubdirectory("basil-beatmap-tests-").FullName;
	private readonly WebApplicationFactory<Program> _factory;
	private Beatmap? _byFilename;
	private Beatmapset? _mapset;
	private Beatmap? _oneBeatmap;
	private byte[]? _replayBytes;
	private ScoreOwner? _scoreOwner;
	private IReadOnlyList<IReadOnlyList<Beatmap>> _searchResults = [];
	private int _searchTotal;
	private IReadOnlyList<Beatmap> _setBeatmaps = [];

	public BeatmapsetEndpointTests(WebApplicationFactory<Program> factory)
	{
		var maps = Substitute.For<IBeatmapRepository>();
		maps.FetchOneAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int?>(),
				Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(call => call.ArgAt<string?>(2) is not null ? _byFilename : _oneBeatmap);
		maps.FetchAllBySetIdAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(call => _setBeatmaps.Count > 0 && _setBeatmaps[0].Beatmapset.Id == call.ArgAt<int>(0)
				? _setBeatmaps
				: []);
		maps.SearchAsync(Arg.Any<BeatmapsetSearchFilters>(), Arg.Any<GameMode?>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<CancellationToken>())
			.Returns(_ => _searchResults);
		maps.SearchCountAsync(Arg.Any<BeatmapsetSearchFilters>(), Arg.Any<GameMode?>(), Arg.Any<CancellationToken>())
			.Returns(_ => _searchTotal);

		var mapsets = Substitute.For<IBeatmapsetRepository>();
		mapsets.FetchByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(call => _mapset?.Id == call.ArgAt<int>(0) ? _mapset : null);

		var scores = Substitute.For<IScoreRepository>();
		scores.FetchOwnerAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_ => _scoreOwner);

		var replayStorage = Substitute.For<IReplayStorage>();
		replayStorage.ReadAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_ => _replayBytes);

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
				services.AddSingleton(TestDoubles.FixedAdminKeySettingsRepository());
				services.AddSingleton(maps);
				services.AddSingleton(mapsets);
				services.AddSingleton(scores);
				services.AddSingleton(replayStorage);
				services.AddSingleton(Options.Create(new StorageOptions
				{
					ReplaysPath = Path.Combine(_dataDir, "Replays"),
					AvatarsPath = Path.Combine(_dataDir, "Avatars"),
					MapsetsPath = Path.Combine(_dataDir, "Mapsets"),
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

	private static HttpRequestMessage MakeRequest(HttpMethod method, string path, string host = "api.test.local")
	{
		return new HttpRequestMessage(method, path) { Headers = { Host = host } };
	}

	private static Beatmapset MakeMapset(int id)
	{
		return new Beatmapset(id, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);
	}

	private static Beatmap MakeBeatmap(int id, Beatmapset beatmapset, string filename = "diff.osu")
	{
		return new Beatmap(new string('a', 32), id, beatmapset, "Normal", filename,
			new Difficulty(GameMode.Standard, 180, TimeSpan.FromSeconds(100), 4, 9, 8, 5, 6.5),
			new OsuBeatmapObjectCounts { MaxCombo = 500 });
	}

	private string MapsetFolder(int setId)
	{
		var folder = Path.Combine(_dataDir, "Mapsets", $"{setId} Artist - Title");
		Directory.CreateDirectory(folder);
		return folder;
	}

	// ---- GET /beatmapsets/{mapsetId} ----

	[Fact]
	public async Task GetBeatmapset_UnknownId_ReturnsNotFound()
	{
		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/999"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task GetBeatmapset_KnownId_ReturnsInfoWithBeatmapsInline()
	{
		var mapset = MakeMapset(100);
		_mapset = mapset;
		_setBeatmaps = [MakeBeatmap(1, mapset, "diff1.osu"), MakeBeatmap(2, mapset, "diff2.osu")];

		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/100"));
		var body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Contains("\"artist\":\"Artist\"", body);
		Assert.Contains("\"beatmaps\"", body);
	}

	[Fact]
	public async Task GetBeatmapset_Private_NonAdmin_ReturnsNotFound()
	{
		var mapset = MakeMapset(101) with { IsPrivate = true };
		_mapset = mapset;
		_setBeatmaps = [MakeBeatmap(1, mapset)];

		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/101"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	// ---- GET /beatmapsets/{mapsetId}/{beatmapId} (info) ----

	[Fact]
	public async Task BeatmapInfo_UnknownId_ReturnsNotFound()
	{
		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/100/999"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task BeatmapInfo_KnownId_ReturnsJson()
	{
		var mapset = MakeMapset(100);
		_oneBeatmap = MakeBeatmap(1, mapset);

		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/100/1"));
		var body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Contains("\"version\":\"Normal\"", body);
		// filename is deliberately never serialized (Phase 2's BeatmapView drops it — internal-only,
		// still used server-side for ingestion/`GET /web/beatmaps/{filename}`).
		Assert.DoesNotContain("\"filename\"", body);
	}

	// ---- GET /beatmapsets/{mapsetId}/{beatmapId}/download ----

	[Fact]
	public async Task DownloadBeatmap_Api_RedirectsToAssetsHost()
	{
		var response = await _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/100/1/download"));

		Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
		Assert.Equal("https://assets.test.local/beatmapsets/100/1/download", response.Headers.Location?.ToString());
	}

	[Fact]
	public async Task DownloadBeatmap_UnknownId_ReturnsNotFound()
	{
		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/100/999/download", "assets.test.local"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task DownloadBeatmap_FileMissingOnDisk_ReturnsNotFound()
	{
		var mapset = MakeMapset(100);
		_oneBeatmap = MakeBeatmap(1, mapset);

		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/100/1/download", "assets.test.local"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task DownloadBeatmap_FileExists_ReturnsCorrectMimeType()
	{
		var mapset = MakeMapset(100);
		_oneBeatmap = MakeBeatmap(1, mapset);
		var folder = MapsetFolder(100);
		await File.WriteAllTextAsync(Path.Combine(folder, "diff.osu"), "osu file format v14");

		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/100/1/download", "assets.test.local"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("application/x-osu-beatmap", response.Content.Headers.ContentType?.MediaType);
	}

	[Fact]
	public async Task DownloadVideo_UnknownBeatmap_ReturnsNotFound()
	{
		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/100/999/video", "assets.test.local"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task DownloadVideo_NoVideoDeclared_ReturnsNotFound()
	{
		var mapset = MakeMapset(400);
		_oneBeatmap = MakeBeatmap(1, mapset);
		var folder = MapsetFolder(400);
		await File.WriteAllTextAsync(Path.Combine(folder, "diff.osu"), "osu file format v14\n[Events]\n");

		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/400/1/video", "assets.test.local"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task DownloadVideo_VideoFileMissingOnDisk_ReturnsNotFound()
	{
		var mapset = MakeMapset(401);
		_oneBeatmap = MakeBeatmap(1, mapset);
		var folder = MapsetFolder(401);
		await File.WriteAllTextAsync(Path.Combine(folder, "diff.osu"), OsuFileWithVideo);

		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/401/1/video", "assets.test.local"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task DownloadVideo_FileExists_ReturnsCorrectMimeType()
	{
		var mapset = MakeMapset(402);
		_oneBeatmap = MakeBeatmap(1, mapset);
		var folder = MapsetFolder(402);
		await File.WriteAllTextAsync(Path.Combine(folder, "diff.osu"), OsuFileWithVideo);
		await File.WriteAllBytesAsync(Path.Combine(folder, "video.mp4"), [1]);

		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/402/1/video", "assets.test.local"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
	}

	// ---- GET /beatmapsets/{mapsetId}/download ----

	[Fact]
	public async Task DownloadBeatmapset_NoFolder_ReturnsNotFound()
	{
		var mapset = MakeMapset(200);
		_setBeatmaps = [MakeBeatmap(1, mapset)];

		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/200/download", "assets.test.local"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task DownloadBeatmapset_FolderExists_ReturnsCorrectMimeType()
	{
		var mapset = MakeMapset(300);
		_setBeatmaps = [MakeBeatmap(1, mapset)];
		var folder = MapsetFolder(300);
		await File.WriteAllTextAsync(Path.Combine(folder, "diff.osu"), "osu file format v14");

		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/300/download", "assets.test.local"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("application/x-osu-beatmap-archive", response.Content.Headers.ContentType?.MediaType);
	}

	/// <summary>
	///     Regression test (Issue #4): "GET /beatmapsets/{id}/download should support No Video (?noVideo=1)."
	/// </summary>
	[Fact]
	public async Task DownloadBeatmapset_NoVideoParam_OmitsVideoFileFromArchive()
	{
		var mapset = MakeMapset(310);
		_setBeatmaps = [MakeBeatmap(1, mapset)];
		var folder = MapsetFolder(310);
		await File.WriteAllTextAsync(Path.Combine(folder, "diff.osu"), "osu file format v14");
		await File.WriteAllBytesAsync(Path.Combine(folder, "bg.mp4"), [1, 2, 3]);

		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/310/download?noVideo=1", "assets.test.local"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		using var archive = new ZipArchive(await response.Content.ReadAsStreamAsync());
		Assert.Contains(archive.Entries, e => e.Name == "diff.osu");
		Assert.DoesNotContain(archive.Entries, e => e.Name == "bg.mp4");
	}

	[Fact]
	public async Task DownloadBeatmapset_NoQueryParam_IncludesVideoFileInArchive()
	{
		var mapset = MakeMapset(320);
		_setBeatmaps = [MakeBeatmap(1, mapset)];
		var folder = MapsetFolder(320);
		await File.WriteAllTextAsync(Path.Combine(folder, "diff.osu"), "osu file format v14");
		await File.WriteAllBytesAsync(Path.Combine(folder, "bg.mp4"), [1, 2, 3]);

		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/320/download", "assets.test.local"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		using var archive = new ZipArchive(await response.Content.ReadAsStreamAsync());
		Assert.Contains(archive.Entries, e => e.Name == "bg.mp4");
	}

	// ---- GET /beatmapsets/{mapsetId}/background ----

	[Fact]
	public async Task MapsetBackground_Api_RedirectsToAssetsHost()
	{
		var response = await _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/803/background"));

		Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
		Assert.Equal("https://assets.test.local/beatmapsets/803/background", response.Headers.Location?.ToString());
	}

	[Fact]
	public async Task MapsetBackground_UnknownId_ReturnsNotFound()
	{
		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/800/background", "assets.test.local"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task MapsetBackground_Private_NonAdmin_ReturnsNotFound()
	{
		var mapset = MakeMapset(801) with { IsPrivate = true, BackgroundFile = "bg.jpg" };
		_mapset = mapset;
		var folder = MapsetFolder(801);
		await File.WriteAllBytesAsync(Path.Combine(folder, "bg.jpg"), [1]);

		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/801/background", "assets.test.local"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task MapsetBackground_NoPreviewRecorded_ReturnsNotFound()
	{
		_mapset = MakeMapset(802);
		MapsetFolder(802);

		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/802/background", "assets.test.local"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task MapsetBackground_FileExists_ReturnsCorrectMimeType()
	{
		_mapset = MakeMapset(803) with { BackgroundFile = "bg.png" };
		var folder = MapsetFolder(803);
		await File.WriteAllBytesAsync(Path.Combine(folder, "bg.png"), [1]);

		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/803/background", "assets.test.local"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
	}

	// ---- GET /beatmapsets/{mapsetId}/storyboard ----

	[Fact]
	public async Task Storyboard_NoFolder_ReturnsNotFound()
	{
		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/400/storyboard", "assets.test.local"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task Storyboard_FolderExistsNoOsb_ReturnsNotFound()
	{
		MapsetFolder(500);

		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/500/storyboard", "assets.test.local"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task Storyboard_FolderHasOsbFile_ReturnsCorrectMimeType()
	{
		var folder = MapsetFolder(600);
		await File.WriteAllTextAsync(Path.Combine(folder, "storyboard.osb"), "[Events]");

		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/600/storyboard", "assets.test.local"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("application/x-osu-storyboard", response.Content.Headers.ContentType?.MediaType);
	}

	// ---- Admin's old single-lookup route is gone (DELETE at the same template still exists —
	// see AdminManagementEndpointTests for DELETE coverage) ----

	[Fact]
	public async Task OldAdminBeatmapLookup_GetNoLongerSupported()
	{
		var request = MakeRequest(HttpMethod.Get, "/beatmaps/1");
		request.Headers.Add("Authorization", "Bearer correct-key");

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	// ---- Pre-existing download routes' MIME-type fixes ----

	[Fact]
	public async Task MapFile_Exists_ReturnsCorrectMimeType()
	{
		_byFilename = MakeBeatmap(1, MakeMapset(700), "Some Map.osu");
		var folder = MapsetFolder(700);
		await File.WriteAllTextAsync(Path.Combine(folder, "Some Map.osu"), "osu file format v14");

		var request = new HttpRequestMessage(HttpMethod.Get, "/web/beatmaps/Some%20Map.osu")
			{ Headers = { Host = "osu.test.local" } };
		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("application/x-osu-beatmap", response.Content.Headers.ContentType?.MediaType);
	}

	[Fact]
	public async Task ReplayDownload_Exists_ReturnsCorrectMimeType()
	{
		_scoreOwner = new ScoreOwner(1, GameMode.Standard);
		_replayBytes = [1, 2, 3];

		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/scores/1/replay"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("application/x-osu-replay", response.Content.Headers.ContentType?.MediaType);
	}

	// ---- GET /beatmapsets/search ----

	[Fact]
	public async Task SearchBeatmapsets_InvalidMode_ReturnsBadRequest()
	{
		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/search?mode=9"));

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task SearchBeatmapsets_MatchingSets_ReturnsPagedSummaries()
	{
		var mapset = MakeMapset(200);
		_searchResults = [[MakeBeatmap(1, mapset), MakeBeatmap(2, mapset)]];
		_searchTotal = 1;

		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/search?q=camellia"));
		var body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Contains("\"totalRecords\":1", body);
		Assert.Contains("\"id\":200", body);
		Assert.Contains("\"beatmapCount\":2", body);
	}

	[Fact]
	public async Task SearchBeatmapsets_NoMatches_ReturnsEmptyPage()
	{
		_searchResults = [];
		_searchTotal = 0;

		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/search?q=stars%3E9"));
		var body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Contains("\"totalRecords\":0", body);
		Assert.Contains("\"data\":[]", body);
	}
}