using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Settings;
using Basil.Application.Configurations;
using Basil.Application.Services.Beatmaps;
using Basil.Domain.Beatmaps;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Application.Tests.Services.Beatmaps;

/// <summary>Verifies `DirectSearchService`'s beatmap search behavior (query parsing, mirror fallback, result shaping).</summary>
public class DirectSearchServiceTests
{
	private readonly IBeatmapRepository _beatmaps = Substitute.For<IBeatmapRepository>();
	private readonly IMirrorSearchClient _mirrorClient = Substitute.For<IMirrorSearchClient>();
	private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();

	private DirectSearchService MakeService()
	{
		var mirror = new MirrorService(_settings, Options.Create(new MirrorOptions()),
			NullLogger<MirrorService>.Instance);
		return new DirectSearchService(_beatmaps, _mirrorClient, mirror, NullLogger<DirectSearchService>.Instance);
	}

	[Theory]
	[InlineData("Newest")]
	// ASP.NET Core query-string binding decodes a literal "+" as a space before this service ever
	// sees it — these must match that decoded form, not the raw "Top+Rated"/"Most+Played" a client
	// puts on the wire, or the sentinel never matches and the filter/tab silently returns nothing.
	[InlineData("Top Rated")]
	[InlineData("Most Played")]
	public async Task NonTextQuery_PassesEmptyFiltersThrough(string query)
	{
		_beatmaps.SearchAsync(BeatmapsetSearchFilters.Empty, null, 0, 100).Returns([]);

		await MakeService().SearchAsync(new DirectSearchRequest(query, -1, 0));

		await _beatmaps.Received(1)
			.SearchAsync(BeatmapsetSearchFilters.Empty, null, 0, 100, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task TextQuery_ParsedAsKeywords()
	{
		var expected = new BeatmapsetSearchFilters("camellia");
		_beatmaps.SearchAsync(expected, null, 0, 100).Returns([]);

		await MakeService().SearchAsync(new DirectSearchRequest("camellia", -1, 0));

		await _beatmaps.Received(1).SearchAsync(expected, null, 0, 100, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task ModeNotMinusOne_FiltersByMode()
	{
		_beatmaps.SearchAsync(BeatmapsetSearchFilters.Empty, GameMode.Taiko, 0, 100).Returns([]);

		await MakeService().SearchAsync(new DirectSearchRequest("Newest", 1, 0));

		await _beatmaps.Received(1)
			.SearchAsync(BeatmapsetSearchFilters.Empty, GameMode.Taiko, 0, 100, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task PageNum_MultipliedByOneHundredForOffset()
	{
		_beatmaps.SearchAsync(BeatmapsetSearchFilters.Empty, null, 200, 100).Returns([]);

		await MakeService().SearchAsync(new DirectSearchRequest("Newest", -1, 2));

		await _beatmaps.Received(1)
			.SearchAsync(BeatmapsetSearchFilters.Empty, null, 200, 100, Arg.Any<CancellationToken>());
	}

	// ---- SearchFormattedAsync: orchestrates local vs. mirror ----

	[Fact]
	public async Task SearchFormattedAsync_NoSearchMirrorConfigured_UsesLocal()
	{
		_beatmaps.SearchAsync(BeatmapsetSearchFilters.Empty, null, 0, 100)
			.Returns([[MakeBeatmap(1, 100, "Hyper", 6.5)]]);

		var response = await MakeService().SearchFormattedAsync(new DirectSearchRequest("Newest", -1, 0));

		Assert.Contains("100.osz|Artist|Title|cmyui|", response);
		await _mirrorClient.DidNotReceiveWithAnyArgs()
			.SearchAsync(default!, default, default, default, default);
	}

	[Fact]
	public async Task SearchFormattedAsync_MirrorConfiguredAndSucceeds_UsesMirrorNotLocal()
	{
		_settings.GetAsync("Mirror:SearchEndpoint", Arg.Any<CancellationToken>()).Returns("https://mirror.local/search");
		_beatmaps.SearchAsync(BeatmapsetSearchFilters.Empty, null, 0, 100)
			.Returns([[MakeBeatmap(1, 100, "Hyper", 6.5)]]);
		var mirrorSet = new MirrorSearchSet("MirrorArtist", "MirrorTitle", "MirrorCreator", 4,
			"2020-01-01 00:00:00", 200, true, [new MirrorSearchBeatmap(5.5, "Insane", 4, 8, 9, 5, 0)]);
		_mirrorClient.SearchAsync("https://mirror.local/search", null, null, 100, 0, Arg.Any<CancellationToken>())
			.Returns([mirrorSet]);

		var response = await MakeService().SearchFormattedAsync(new DirectSearchRequest("Newest", -1, 0));

		Assert.Contains("200.osz|MirrorArtist|MirrorTitle|MirrorCreator|4|10.0|2020-01-01 00:00:00|200|1|",
			response);
		Assert.DoesNotContain("Artist|Title|cmyui", response);
		await _beatmaps.DidNotReceiveWithAnyArgs()
			.SearchAsync(default, default, default, default, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task SearchFormattedAsync_MirrorConfiguredButFails_FallsBackToLocal()
	{
		_settings.GetAsync("Mirror:SearchEndpoint", Arg.Any<CancellationToken>()).Returns("https://mirror.local/search");
		_beatmaps.SearchAsync(BeatmapsetSearchFilters.Empty, null, 0, 100)
			.Returns([[MakeBeatmap(1, 100, "Hyper", 6.5)]]);
		_mirrorClient.SearchAsync("https://mirror.local/search", null, null, 100, 0, Arg.Any<CancellationToken>())
			.Returns((IReadOnlyList<MirrorSearchSet>?)null);

		var response = await MakeService().SearchFormattedAsync(new DirectSearchRequest("Newest", -1, 0));

		Assert.Contains("100.osz|Artist|Title|cmyui|", response);
	}

	private static Beatmap MakeBeatmap(int id, int setId, string version, double diff, string artist = "Artist",
		string title = "Title")
	{
		var beatmapset = new Beatmapset(setId, artist, title, "cmyui",
			new DateTime(2020, 3, 15, 10, 30, 0, DateTimeKind.Utc),
			new DateTime(2020, 3, 15, 10, 30, 0, DateTimeKind.Utc));
		return new Beatmap(
			new string('0', 32), id, beatmapset, version, "file.osu",
			new Difficulty(GameMode.Standard, 180, TimeSpan.FromSeconds(100), 4, 9, 8, 5, diff),
			new OsuBeatmapObjectCounts { MaxCombo = 500 });
	}

	[Fact]
	public void Format_EmptyList_ReturnsZero()
	{
		Assert.Equal("0", DirectSearchService.Format([]));
	}

	[Fact]
	public void Format_SingleSetSingleDiff_MatchesFormatString()
	{
		var set = new List<Beatmap> { MakeBeatmap(1, 100, "Hyper", 6.5) };

		var response = DirectSearchService.Format([set]);

		var expectedSetLine =
			"100.osz|Artist|Title|cmyui|2|10.0|2020-03-15 10:30:00|100|0|0|0|0|0|[6.50⭐] Hyper {CS: 4 / OD: 8 / AR: 9 / HP: 5}@0";
		Assert.Equal("1\n" + expectedSetLine, response);
	}

	[Fact]
	public void Format_MultipleDiffsInSet_JoinedByComma()
	{
		var set = new List<Beatmap>
		{
			MakeBeatmap(1, 100, "Easy", 2.0),
			MakeBeatmap(2, 100, "Hard", 4.5)
		};

		var response = DirectSearchService.Format([set]);

		Assert.Contains("[2.00⭐] Easy {CS: 4 / OD: 8 / AR: 9 / HP: 5}@0,[4.50⭐] Hard {CS: 4 / OD: 8 / AR: 9 / HP: 5}@0",
			response);
	}

	[Fact]
	public void Format_PipeInMetadata_ReplacedWithI()
	{
		var set = new List<Beatmap> { MakeBeatmap(1, 100, "Di|ff", 1.0, "Art|ist", "Ti|tle") };

		var response = DirectSearchService.Format([set]);

		Assert.Contains("100.osz|ArtIist|TiItle|cmyui|", response);
		Assert.Contains("DiIff", response);
	}

	[Fact]
	public void Format_100Sets_ReportsCountAs101()
	{
		var sets = Enumerable.Range(0, 100)
			.Select(IReadOnlyList<Beatmap> (i) => new List<Beatmap> { MakeBeatmap(i, i, "Sr", 1.0) }).ToList();

		var response = DirectSearchService.Format(sets);

		Assert.StartsWith("101\n", response);
	}

	[Fact]
	public void Format_99Sets_ReportsLiteralCount()
	{
		var sets = Enumerable.Range(0, 99)
			.Select(IReadOnlyList<Beatmap> (i) => new List<Beatmap> { MakeBeatmap(i, i, "Sr", 1.0) }).ToList();

		var response = DirectSearchService.Format(sets);

		Assert.StartsWith("99\n", response);
	}

	[Fact]
	public void FormatSet_Null_ReturnsEmptyString()
	{
		Assert.Equal("", DirectSearchService.FormatSet(null));
	}

	[Fact]
	public void FormatSet_UsesRawStatusValue_NoDiffsField_NoPipeEscaping()
	{
		var bmap = MakeBeatmap(1, 100, "Hyper", 6.5, "Art|ist");

		var response = DirectSearchService.FormatSet(bmap);

		Assert.Equal("100.osz|Art|ist|Title|cmyui|3|10.0|2020-03-15 10:30:00|100|0|0|0|0|0", response);
	}

	// ---- FormatMirror ----

	[Fact]
	public void FormatMirror_EmptyList_ReturnsZero()
	{
		Assert.Equal("0", DirectSearchService.FormatMirror([]));
	}

	[Fact]
	public void FormatMirror_SingleSet_MatchesFormatString()
	{
		var set = new MirrorSearchSet("Artist", "Title", "cmyui", 2, "2020-03-15 10:30:00", 100, false,
			[new MirrorSearchBeatmap(6.5, "Hyper", 4, 8, 9, 5, 0)]);

		var response = DirectSearchService.FormatMirror([set]);

		var expectedLine =
			"100.osz|Artist|Title|cmyui|2|10.0|2020-03-15 10:30:00|100|0|0|0|0|0|[6.50⭐] Hyper {CS: 4 / OD: 8 / AR: 9 / HP: 5}@0";
		Assert.Equal("1\n" + expectedLine, response);
	}

	[Fact]
	public void FormatMirror_HasVideoTrue_SetsFlagToOne()
	{
		var set = new MirrorSearchSet("Artist", "Title", "cmyui", 2, "2020-03-15 10:30:00", 100, true, []);

		var response = DirectSearchService.FormatMirror([set]);

		Assert.Contains("100|1|0|0|0|0|", response);
	}

	[Fact]
	public void FormatMirror_PipeInMetadata_ReplacedWithI()
	{
		var set = new MirrorSearchSet("Art|ist", "Ti|tle", "cmyui", 2, "2020-03-15 10:30:00", 100, false,
			[new MirrorSearchBeatmap(1.0, "Di|ff", 4, 8, 9, 5, 0)]);

		var response = DirectSearchService.FormatMirror([set]);

		Assert.Contains("100.osz|ArtIist|TiItle|cmyui|", response);
		Assert.Contains("DiIff", response);
	}

	[Fact]
	public void FormatMirror_NullBeatmaps_EmptyDiffsField()
	{
		var set = new MirrorSearchSet("Artist", "Title", "cmyui", 2, "2020-03-15 10:30:00", 100, false, null);

		var response = DirectSearchService.FormatMirror([set]);

		Assert.EndsWith("|100|0|0|0|0|0|", response);
	}
}