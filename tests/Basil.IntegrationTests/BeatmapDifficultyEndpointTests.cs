using System.Net;
using System.Net.Http.Json;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Configurations;
using Basil.Application.Formats;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Basil.Web;
using Basil.Web.OpenApi;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers `GET /beatmapsets/{mapsetId}/{beatmapId}/difficulty` end-to-end against a real
///     analyzable `.osu` file (the same fixture <c>PpyOsuCalculatorTests</c> uses, so the recorded
///     NoMod/HardRock star ratings there double as a cross-check here) — not just route wiring, since
///     this endpoint's whole point is running the real ppy.osu.Game difficulty calculator.
/// </summary>
public class BeatmapDifficultyEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
	// Verbatim copy of Basil.Infrastructure.Tests/Fixtures/vivid_osu_file.osu — PpyOsuCalculatorTests
	// records NoMod Sr=4.8750450142072701 (-> 4.88 rounded) and HardRock Sr=5.9296060838721534 (-> 5.93
	// rounded) against this exact content.
	private const string FixtureOsuContent = """
	                                         osu file format v3

	                                         [General]
	                                         AudioFilename: 01_-_vivid.mp3
	                                         AudioLeadIn: 2000
	                                         AudioHash: f9e55f878282eba37a8da909fcc40994
	                                         PreviewTime: -1
	                                         SampleSet: Normal
	                                         EditorBookmarks: 3317,5460,8942,14389,16085,17514,19300,22692,25907,28942,31800,34300,37335

	                                         [Metadata]
	                                         Title:Vivid
	                                         Artist:FAIRY FORE
	                                         Creator:Hitoshirenu Shourai
	                                         Version:Insane

	                                         [Difficulty]
	                                         HPDrainRate:2
	                                         CircleSize:6
	                                         OverallDifficulty:7
	                                         SliderMultiplier: 1
	                                         SliderTickRate: 2

	                                         [Events]
	                                         0,0,"Chocobos.jpg"
	                                         3,0,0,0,255

	                                         [TimingPoints]
	                                         520,357.142857142857

	                                         [HitObjects]
	                                         256,192,520,1,0,
	                                         224,192,609,1,0,
	                                         192,192,698,1,0,
	                                         208,160,787,1,0,
	                                         224,144,877,5,0,
	                                         256,144,966,1,0,
	                                         288,144,1055,1,0,
	                                         320,160,1144,1,0,
	                                         320,192,1234,5,0,
	                                         304,220,1323,1,0,
	                                         288,240,1412,1,0,
	                                         256,240,1502,1,0,
	                                         224,240,1591,5,0,
	                                         192,240,1680,1,0,
	                                         160,240,1769,1,0,
	                                         128,224,1859,1,0,
	                                         128,192,1948,5,0,
	                                         144,168,2037,1,0,
	                                         160,144,2127,1,0,
	                                         177,120,2216,1,0,
	                                         192,96,2305,5,0,
	                                         224,96,2394,1,0,
	                                         256,96,2484,1,0,
	                                         288,96,2573,1,0,
	                                         320,96,2662,5,0,
	                                         339,122,2752,1,0,
	                                         352,144,2841,1,0,
	                                         373,173,2930,1,0,
	                                         384,192,3019,5,0,
	                                         373,220,3109,1,0,
	                                         360,248,3198,1,0,
	                                         337,272,3287,1,0,
	                                         320,288,3377,5,0,
	                                         288,288,3466,1,0,
	                                         256,288,3555,1,0,
	                                         224,288,3644,1,0,
	                                         192,288,3734,5,0,
	                                         136,288,3912,1,0,
	                                         96,256,4091,5,0,
	                                         72,216,4180,1,0,
	                                         72,176,4269,1,0,
	                                         80,144,4359,1,0,
	                                         96,120,4448,5,0,
	                                         128,88,4627,1,0,
	                                         160,48,4805,5,0,
	                                         192,48,4895,1,0,
	                                         224,48,4984,1,0,
	                                         256,48,5073,1,0,
	                                         288,48,5162,5,0,
	                                         352,48,5341,1,0,
	                                         416,128,5698,6,0,B|416:128|416:192,1,49
	                                         416,272,6234,5,0,
	                                         416,312,6323,1,0,
	                                         416,352,6412,1,0,
	                                         376,352,6502,1,0,
	                                         336,352,6591,5,0,
	                                         336,272,6770,1,0,
	                                         256,272,6948,5,0,
	                                         256,312,7037,1,0,
	                                         256,352,7127,1,0,
	                                         216,352,7216,1,0,
	                                         176,352,7305,5,0,
	                                         176,272,7484,1,0,
	                                         176,192,7662,5,0,
	                                         136,192,7752,1,0,
	                                         96,192,7841,1,0,
	                                         64,160,7930,1,0,
	                                         32,128,8020,5,0,
	                                         32,48,8198,1,0,
	                                         112,112,8555,6,0,B|112:112|112:48,1,49
	                                         224,32,9091,5,0,
	                                         224,64,9180,1,0,
	                                         224,96,9270,1,0,
	                                         256,96,9359,1,0,
	                                         288,96,9448,5,0,
	                                         288,160,9627,1,0,
	                                         224,160,9805,5,0,
	                                         224,192,9895,1,0,
	                                         224,224,9984,1,0,
	                                         256,224,10073,1,0,
	                                         288,224,10162,5,0,
	                                         288,288,10341,1,0,
	                                         224,288,10520,5,0,
	                                         224,320,10609,1,0,
	                                         224,352,10698,1,0,
	                                         256,352,10787,1,0,
	                                         288,352,10877,5,0,
	                                         352,352,11055,1,0,
	                                         352,288,11234,5,0,
	                                         352,256,11323,1,0,
	                                         352,224,11412,1,0,
	                                         384,224,11502,1,0,
	                                         416,224,11591,5,0,
	                                         416,288,11769,1,0,
	                                         480,256,11948,5,0,
	                                         480,224,12037,1,0,
	                                         480,192,12127,1,0,
	                                         480,160,12216,1,0,
	                                         416,160,12305,5,0,
	                                         352,160,12484,1,0,
	                                         352,96,12662,5,0,
	                                         384,96,12752,1,0,
	                                         416,96,12841,1,0,
	                                         448,96,12930,1,0,
	                                         416,32,13019,5,0,
	                                         352,32,13198,1,0,
	                                         288,32,13377,6,0,B|288:32|176:32,1,98
	                                         192,96,13912,2,0,B|192:96|304:96,1,98
	                                         """;

	private readonly string _dataDir = Directory.CreateTempSubdirectory("basil-difficulty-tests-").FullName;
	private readonly WebApplicationFactory<Program> _factory;
	private Beatmap? _beatmap;
	private Beatmapset? _mapset;

	public BeatmapDifficultyEndpointTests(WebApplicationFactory<Program> factory)
	{
		var maps = Substitute.For<IBeatmapRepository>();
		maps.FetchOneAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int?>(),
				Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				var includePrivate = call.ArgAt<bool>(4);
				return _beatmap is not null && (includePrivate || !_beatmap.Beatmapset.IsPrivate) ? _beatmap : null;
			});
		maps.FetchAllBySetIdAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(_ => _beatmap is null ? [] : [_beatmap]);

		var mapsets = Substitute.For<IBeatmapsetRepository>();
		mapsets.FetchByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(_ => _mapset?.Id == _beatmap?.Beatmapset.Id ? _mapset : null);

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

	private static HttpRequestMessage MakeRequest(string path)
	{
		return new HttpRequestMessage(HttpMethod.Get, path) { Headers = { Host = "api.test.local" } };
	}

	private void SeedBeatmap(int mapsetId, int beatmapId, bool isPrivate = false)
	{
		_mapset = new Beatmapset(mapsetId, "FAIRY FORE", "Vivid", "Hitoshirenu Shourai", DateTime.UnixEpoch,
			DateTime.UnixEpoch, IsPrivate: isPrivate);
		_beatmap = new Beatmap(new string('a', 32), beatmapId, _mapset, "Insane", "vivid.osu",
			new Difficulty(GameMode.Standard, 0, TimeSpan.Zero, 0, 0, 0, 0, 0), new OsuBeatmapObjectCounts());

		var folder = Path.Combine(_dataDir, "Mapsets", $"{mapsetId} FAIRY FORE - Vivid");
		Directory.CreateDirectory(folder);
		File.WriteAllText(Path.Combine(folder, "vivid.osu"), FixtureOsuContent);
	}

	[Fact]
	public async Task GetDifficulty_NoMod_ReturnsRecordedStarRating()
	{
		SeedBeatmap(9001, 1);
		using var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest("/beatmapsets/9001/1/difficulty"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var body = await response.Content.ReadFromJsonAsync<Envelope<DifficultyResultShape>>(BasilJsonOptions.Instance);
		Assert.NotNull(body?.Data);
		Assert.Equal(Mods.NoMod, body.Data.Mods);
		Assert.Equal(4.88, body.Data.Beatmap.Difficulty.Sr, 2);
	}

	[Fact]
	public async Task GetDifficulty_HardRock_MatchesRecordedReference_AndDiffersFromNoMod()
	{
		SeedBeatmap(9002, 2);
		using var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest("/beatmapsets/9002/2/difficulty?mods=16"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var body = await response.Content.ReadFromJsonAsync<Envelope<DifficultyResultShape>>(BasilJsonOptions.Instance);
		Assert.NotNull(body?.Data);
		Assert.Equal(Mods.HardRock, body.Data.Mods);
		Assert.Equal(5.93, body.Data.Beatmap.Difficulty.Sr, 2);
		Assert.Equal(7.8, body.Data.Beatmap.Difficulty.Cs, 1); // raw CS=6 * 1.3
	}

	[Fact]
	public async Task GetDifficulty_InvalidMode_ReturnsBadRequest()
	{
		SeedBeatmap(9003, 3);
		using var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest("/beatmapsets/9003/3/difficulty?mode=9"));

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task GetDifficulty_UnknownBeatmap_ReturnsNotFound()
	{
		using var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest("/beatmapsets/9004/999/difficulty"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task GetDifficulty_PrivateMapsetWithoutAdminKey_ReturnsNotFound()
	{
		SeedBeatmap(9005, 5, true);
		using var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest("/beatmapsets/9005/5/difficulty"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	// Local shadow of BeatmapsetRoutes.BeatmapDifficultyResult — that type is internal to Basil.Web, so
	// the test deserializes into its own matching shape instead (same pattern as ScoreEndpointTests'
	// ScoreShape), pulling in only the fields these tests actually assert on.
	private sealed record DifficultyResultShape(Mods Mods, BeatmapShape Beatmap);

	private sealed record BeatmapShape(Difficulty Difficulty);
}