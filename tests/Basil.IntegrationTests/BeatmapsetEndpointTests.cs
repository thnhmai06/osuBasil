using System.Net;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Configuration;
using Basil.Domain.Beatmaps;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers the public `/beatmapsets` routes (info + downloads) — the old singular `/beatmap/{id}`
///     and `/beatmap/{id}/download` routes were dropped in favor of `GET /beatmapsets/{mapsetId}`
///     (which now embeds each beatmap's id/version/mode inline), `GET
///     /beatmapsets/{mapsetId}/{beatmapId}` (a single difficulty's JSON metadata), and `GET
///     /beatmapsets/{mapsetId}/{beatmapId}/download` (the raw `.osu` file, moved off the bare
///     path). Also covers the MIME-type correctness pass across every download route (osu!'s real
///     per-extension types instead of generic ones).
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
	private Mapset? _mapset;
	private Beatmap? _oneBeatmap;
	private byte[]? _replayBytes;
	private ScoreOwnerRow? _scoreOwner;
	private IReadOnlyList<Beatmap> _setBeatmaps = [];

	public BeatmapsetEndpointTests(WebApplicationFactory<Program> factory)
	{
		var maps = Substitute.For<IBeatmapRepository>();
		maps.FetchOneAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int?>(),
				Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(call => call.ArgAt<string?>(2) is not null ? _byFilename : _oneBeatmap);
		maps.FetchAllBySetIdAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(call => _setBeatmaps.Count > 0 && _setBeatmaps[0].Mapset.Id == call.ArgAt<int>(0)
				? _setBeatmaps
				: []);
		maps.SearchAsync(Arg.Any<string?>(), Arg.Any<GameMode?>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<IReadOnlyList<Beatmap>>>([]));

		var mapsets = Substitute.For<IMapsetRepository>();
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
					["Basil:Bot:CommandPrefix"] = "!",
					["Basil:Server:AdminKey"] = "correct-key"
				});
			});
			builder.ConfigureServices(services =>
			{
				services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions { Path = "" }));
				services.AddSingleton(maps);
				services.AddSingleton(mapsets);
				services.AddSingleton(scores);
				services.AddSingleton(replayStorage);
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

	private static HttpRequestMessage MakeRequest(HttpMethod method, string path, string host = "api.test.local")
	{
		return new HttpRequestMessage(method, path) { Headers = { Host = host } };
	}

	private static Mapset MakeMapset(int id)
	{
		return new Mapset(id, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);
	}

	private static Beatmap MakeBeatmap(int id, Mapset mapset, string filename = "diff.osu")
	{
		return new Beatmap(new string('a', 32), id, mapset, "Normal", filename,
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
	public async Task DownloadBeatmap_UnknownId_ReturnsNotFound()
	{
		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/100/999/download"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task DownloadBeatmap_FileMissingOnDisk_ReturnsNotFound()
	{
		var mapset = MakeMapset(100);
		_oneBeatmap = MakeBeatmap(1, mapset);

		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/100/1/download"));

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
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/100/1/download"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("application/x-osu-beatmap", response.Content.Headers.ContentType?.MediaType);
	}

	[Fact]
	public async Task DownloadVideo_UnknownBeatmap_ReturnsNotFound()
	{
		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/100/999/video"));

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
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/400/1/video"));

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
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/401/1/video"));

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
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/402/1/video"));

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
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/200/download"));

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
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/300/download"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("application/x-osu-beatmap-archive", response.Content.Headers.ContentType?.MediaType);
	}

	// ---- GET /beatmapsets/{mapsetId}/background ----

	[Fact]
	public async Task MapsetBackground_UnknownId_ReturnsNotFound()
	{
		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/800/background"));

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
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/801/background"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task MapsetBackground_NoPreviewRecorded_ReturnsNotFound()
	{
		_mapset = MakeMapset(802);
		MapsetFolder(802);

		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/802/background"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task MapsetBackground_FileExists_ReturnsCorrectMimeType()
	{
		_mapset = MakeMapset(803) with { BackgroundFile = "bg.png" };
		var folder = MapsetFolder(803);
		await File.WriteAllBytesAsync(Path.Combine(folder, "bg.png"), [1]);

		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/803/background"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
	}

	// ---- GET /beatmapsets/{mapsetId}/storyboard ----

	[Fact]
	public async Task Storyboard_NoFolder_ReturnsNotFound()
	{
		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/400/storyboard"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task Storyboard_FolderExistsNoOsb_ReturnsNotFound()
	{
		MapsetFolder(500);

		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/500/storyboard"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task Storyboard_FolderHasOsbFile_ReturnsCorrectMimeType()
	{
		var folder = MapsetFolder(600);
		await File.WriteAllTextAsync(Path.Combine(folder, "storyboard.osb"), "[Events]");

		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/beatmapsets/600/storyboard"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("application/x-osu-storyboard", response.Content.Headers.ContentType?.MediaType);
	}

	// ---- Admin's old single-lookup route is gone (DELETE at the same template still exists —
	// see AdminManagementEndpointTests for DELETE coverage) ----

	[Fact]
	public async Task OldAdminBeatmapLookup_GetNoLongerSupported()
	{
		var request = MakeRequest(HttpMethod.Get, "/beatmaps/1");
		request.Headers.Add("X-Admin-Key", "correct-key");

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
		_scoreOwner = new ScoreOwnerRow(1, GameMode.Standard);
		_replayBytes = [1, 2, 3];

		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/scores/1/replay"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("application/x-osu-replay", response.Content.Headers.ContentType?.MediaType);
	}
}