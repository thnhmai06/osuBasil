using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>
///     Verifies `SqliteBeatmapRepository`'s beatmap lookups by id/md5/filename and upsert behavior.
/// </summary>
public class SqliteBeatmapRepositoryTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
	private readonly SqliteBeatmapsetRepository _beatmapsetRepository =
		new(fixture.ConnectionString, NullLogger<SqliteBeatmapsetRepository>.Instance);

	private readonly SqliteBeatmapRepository _repository =
		new(fixture.ConnectionString, NullLogger<SqliteBeatmapRepository>.Instance);

	private static Beatmapset MakeMapset(int id, string artist = "Camellia",
		string title = "Exit This Earth's Atomosphere", string creator = "cmyui", bool isPrivate = false)
	{
		return new Beatmapset(id, artist, title, creator,
			new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
			IsPrivate: isPrivate);
	}

	private static Beatmap MakeBeatmap(int id, string md5, bool isPrivate = false)
	{
		return new Beatmap(md5, id, MakeMapset(1000 + id, isPrivate: isPrivate), "Hyper",
			$"Camellia - Exit This Earth's Atomosphere (cmyui) [Hyper] {id}.osu",
			new Difficulty(GameMode.Standard, 180.0, TimeSpan.FromSeconds(120), 4.0, 9.0, 8.0, 5.0, 6.5),
			new OsuBeatmapObjectCounts { MaxCombo = 500 });
	}

	private async Task<Beatmap> UpsertBeatmapAsync(Beatmap beatmap)
	{
		await _beatmapsetRepository.UpsertAsync(beatmap.Beatmapset);
		return await _repository.UpsertAsync(beatmap);
	}

	[Fact]
	public async Task UpsertThenFetchByMd5_ReturnsBeatmap()
	{
		var bmap = MakeBeatmap(101, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

		await UpsertBeatmapAsync(bmap);
		var fetched = await _repository.FetchOneAsync(md5: bmap.Md5);

		Assert.NotNull(fetched);
		Assert.Equal(bmap, fetched);
	}

	[Fact]
	public async Task UpsertThenFetch_RoundTripsBackgroundFileAndObjectCounts()
	{
		var bmap = MakeBeatmap(103, "cccccccccccccccccccccccccccccccc") with
		{
			BackgroundFile = "bg.jpg",
			ObjectCounts = new OsuBeatmapObjectCounts
				{ Total = 167, MaxCombo = 500, Circles = 120, Sliders = 45, Spinners = 2 }
		};

		await UpsertBeatmapAsync(bmap);
		var fetched = await _repository.FetchOneAsync(md5: bmap.Md5);

		Assert.NotNull(fetched);
		Assert.Equal("bg.jpg", fetched.BackgroundFile);
		Assert.Equal(bmap.ObjectCounts, fetched.ObjectCounts);
	}

	[Fact]
	public async Task FetchById_ReturnsBeatmap()
	{
		var bmap = MakeBeatmap(102, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
		await UpsertBeatmapAsync(bmap);

		var fetched = await _repository.FetchOneAsync(bmap.Id);

		Assert.NotNull(fetched);
		Assert.Equal(bmap.Md5, fetched.Md5);
	}

	[Fact]
	public async Task FetchByFilename_ReturnsBeatmap()
	{
		var bmap = MakeBeatmap(103, new string('c', 32));
		await UpsertBeatmapAsync(bmap);

		var fetched = await _repository.FetchOneAsync(filename: bmap.Filename);

		Assert.NotNull(fetched);
		Assert.Equal(bmap.Id, fetched.Id);
	}

	[Fact]
	public async Task FetchOne_Unknown_ReturnsNull()
	{
		Assert.Null(await _repository.FetchOneAsync(md5: new string('0', 32)));
	}

	[Fact]
	public async Task Upsert_ExistingId_ReplacesRow()
	{
		var bmap = MakeBeatmap(104, "dddddddddddddddddddddddddddddddd");
		await UpsertBeatmapAsync(bmap);

		var updated = bmap with { ObjectCounts = new OsuBeatmapObjectCounts { MaxCombo = 42 } };
		await UpsertBeatmapAsync(updated);

		var fetched = await _repository.FetchOneAsync(bmap.Id);
		Assert.Equal(42, fetched!.ObjectCounts.MaxCombo);
	}

	[Fact]
	public async Task Upsert_UnresolvedId_ResolvesFromLocalIdFloor()
	{
		var bmap = MakeBeatmap(0, "aa000000000000000000000000000a");

		await _beatmapsetRepository.UpsertAsync(bmap.Beatmapset);
		var resolved = await _repository.UpsertAsync(bmap);

		Assert.True(resolved.Id >= Beatmap.LocalIdFloor);
		var fetched = await _repository.FetchOneAsync(md5: bmap.Md5);
		Assert.Equal(resolved.Id, fetched!.Id);
	}

	[Fact]
	public async Task Upsert_ExistingMd5_KeepsOriginalId_EvenWhenPassedADifferentId()
	{
		var original = MakeBeatmap(107, "bb000000000000000000000000000b");
		var firstResolved = await UpsertBeatmapAsync(original);

		var reupserted = original with
		{
			Id = 999_999, ObjectCounts = new OsuBeatmapObjectCounts { MaxCombo = 7 }
		};
		var secondResolved = await UpsertBeatmapAsync(reupserted);

		Assert.Equal(firstResolved.Id, secondResolved.Id);
		Assert.Equal(7, secondResolved.ObjectCounts.MaxCombo);
	}

	[Fact]
	public async Task DeleteByMd5_RemovesRow()
	{
		var bmap = MakeBeatmap(105, "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee");
		await UpsertBeatmapAsync(bmap);

		await _repository.DeleteByMd5Async(bmap.Md5);

		Assert.Null(await _repository.FetchOneAsync(bmap.Id));
	}

	[Fact]
	public async Task FetchOne_NoParameters_Throws()
	{
		await Assert.ThrowsAsync<ArgumentException>(() => _repository.FetchOneAsync());
	}

	[Fact]
	public async Task FetchOne_BySetId_ReturnsAnyMapInThatSet()
	{
		var bmap = MakeBeatmap(106, "ffffffffffffffffffffffffffffffff");
		await UpsertBeatmapAsync(bmap);

		var fetched = await _repository.FetchOneAsync(setId: bmap.Beatmapset.Id);

		Assert.NotNull(fetched);
		Assert.Equal(bmap.Beatmapset.Id, fetched.Beatmapset.Id);
	}

	[Fact]
	public async Task FetchOne_PrivateMapset_HiddenByDefault_VisibleWithIncludePrivate()
	{
		var bmap = MakeBeatmap(108, "gg00000000000000000000000000gg", true);
		await UpsertBeatmapAsync(bmap);

		Assert.Null(await _repository.FetchOneAsync(bmap.Id));
		var fetched = await _repository.FetchOneAsync(bmap.Id, includePrivate: true);
		Assert.NotNull(fetched);
		Assert.True(fetched.Beatmapset.IsPrivate);
	}

	[Fact]
	public async Task FetchAllBySetId_ExcludesPrivateMapsetByDefault_IncludesWithFlag()
	{
		var setId = 5050;
		var mapset = MakeMapset(setId, isPrivate: true);
		var first = new Beatmap(new string('n', 32), 250, mapset, "Normal", "n.osu",
			new Difficulty(GameMode.Standard, 180.0, TimeSpan.FromSeconds(60), 4.0, 9.0, 8.0, 5.0, 3.0),
			new OsuBeatmapObjectCounts { MaxCombo = 500 });
		var second = new Beatmap(new string('o', 32), 251, mapset, "Hidden", "o.osu",
			new Difficulty(GameMode.Standard, 180.0, TimeSpan.FromSeconds(60), 4.0, 9.0, 8.0, 5.0, 3.0),
			new OsuBeatmapObjectCounts { MaxCombo = 500 });
		await UpsertBeatmapAsync(first);
		await UpsertBeatmapAsync(second);

		var defaultResult = await _repository.FetchAllBySetIdAsync(setId);
		var includingPrivate = await _repository.FetchAllBySetIdAsync(setId, true);

		Assert.Empty(defaultResult);
		Assert.Equal(2, includingPrivate.Count);
	}

	private static Beatmap MakeBeatmap(int id, int setId, string md5, string artist, double diff,
		GameMode mode = GameMode.Standard, bool isPrivate = false)
	{
		return new Beatmap(md5, id, MakeMapset(setId, artist, "Title", isPrivate: isPrivate),
			$"Diff{id}", $"{artist} - Title (cmyui) [Sr{id}].osu",
			new Difficulty(mode, 180.0, TimeSpan.FromSeconds(120), 4.0, 9.0, 8.0, 5.0, diff),
			new OsuBeatmapObjectCounts { MaxCombo = 500 });
	}

	[Fact]
	public async Task SearchAsync_GroupsBySetId_NewestFirst_DiffsAscendingWithinSet()
	{
		var setA = 5001;
		var setB = 5002; // higher set_id -> newer -> should come first
		await UpsertBeatmapAsync(MakeBeatmap(201, setA, new string('f', 32), "Zeta Artist", 5.0));
		await UpsertBeatmapAsync(MakeBeatmap(202, setA, new string('g', 32), "Zeta Artist", 2.0));
		await UpsertBeatmapAsync(MakeBeatmap(203, setB, new string('h', 32), "Alpha Artist", 3.0));

		var results = await _repository.SearchAsync(BeatmapsetSearchFilters.Empty, null, 0, 100);
		var relevant = results.Where(set => set[0].Beatmapset.Id is 5001 or 5002).ToList();

		Assert.Equal(5002, relevant[0][0].Beatmapset.Id);
		Assert.Equal(5001, relevant[1][0].Beatmapset.Id);
		Assert.Equal(2, relevant[1].Count);
		Assert.True(relevant[1][0].Difficulty.Sr < relevant[1][1].Difficulty.Sr);
	}

	[Fact]
	public async Task SearchAsync_FiltersByQueryText_MatchesArtistTitleOrCreator()
	{
		var setId = 5010;
		await UpsertBeatmapAsync(MakeBeatmap(210, setId, new string('i', 32), "UniqueArtistName210", 1.0));

		var results = await _repository.SearchAsync(new BeatmapsetSearchFilters("UniqueArtistName210"), null, 0, 100);

		Assert.Single(results);
		Assert.Equal(setId, results[0][0].Beatmapset.Id);
	}

	[Fact]
	public async Task SearchAsync_FiltersByMode()
	{
		var setId = 5020;
		await UpsertBeatmapAsync(MakeBeatmap(220, setId, new string('j', 32), "ModeFilterArtist220", 1.0,
			GameMode.Taiko));

		var filters = new BeatmapsetSearchFilters("ModeFilterArtist220");
		var matching = await _repository.SearchAsync(filters, GameMode.Taiko, 0, 100);
		var nonMatching = await _repository.SearchAsync(filters, GameMode.Catch, 0, 100);

		Assert.Single(matching);
		Assert.Empty(nonMatching);
	}

	[Fact]
	public async Task SearchAsync_RespectsOffsetAndAmount()
	{
		await UpsertBeatmapAsync(MakeBeatmap(240, 5040, new string('l', 32), "PagingArtist240A", 1.0));
		await UpsertBeatmapAsync(MakeBeatmap(241, 5041, new string('m', 32), "PagingArtist240B", 1.0));

		var pagingFilters = new BeatmapsetSearchFilters("PagingArtist240");
		var page1 = await _repository.SearchAsync(pagingFilters, null, 0, 1);
		var page2 = await _repository.SearchAsync(pagingFilters, null, 1, 1);

		Assert.Single(page1);
		Assert.Single(page2);
		Assert.NotEqual(page1[0][0].Beatmapset.Id, page2[0][0].Beatmapset.Id);
	}

	[Fact]
	public async Task SearchAsync_ExcludesPrivateMapsets()
	{
		var setId = 5060;
		await UpsertBeatmapAsync(MakeBeatmap(260, setId, new string('p', 32), "PrivateSearchArtist260", 1.0,
			isPrivate: true));

		var results =
			await _repository.SearchAsync(new BeatmapsetSearchFilters("PrivateSearchArtist260"), null, 0, 100);

		Assert.Empty(results);
	}

	// ---- Structured filters (Issue #4: "Support searching by ... osu!'s search parameters") ----

	[Fact]
	public async Task SearchAsync_StarsFilter_MatchesOnlyInRange()
	{
		var setId = 5070;
		await UpsertBeatmapAsync(MakeBeatmap(270, setId, new string('q', 32), "StarsFilterArtist270", 3.0));

		var matching = await _repository.SearchAsync(
			new BeatmapsetSearchFilters(Stars: new ComparableFilter<double>(ComparisonOperator.GreaterThan, 2.0)),
			null, 0, 100);
		var nonMatching = await _repository.SearchAsync(
			new BeatmapsetSearchFilters(Stars: new ComparableFilter<double>(ComparisonOperator.GreaterThan, 5.0)),
			null, 0, 100);

		Assert.Contains(matching, set => set[0].Beatmapset.Id == setId);
		Assert.DoesNotContain(nonMatching, set => set[0].Beatmapset.Id == setId);
	}

	[Fact]
	public async Task SearchAsync_CirclesFilter_ReadsFromObjectCountsJson()
	{
		var setId = 5071;
		var mapset = MakeMapset(setId, "CirclesFilterArtist271");
		await UpsertBeatmapAsync(new Beatmap(new string('r', 32), 271, mapset, "Diff",
			"CirclesFilterArtist271 - Title (cmyui) [Diff].osu",
			new Difficulty(GameMode.Standard, 180.0, TimeSpan.FromSeconds(120), 4.0, 9.0, 8.0, 5.0, 3.0),
			new OsuBeatmapObjectCounts { MaxCombo = 500, Circles = 300, Sliders = 50 }));

		var matching = await _repository.SearchAsync(
			new BeatmapsetSearchFilters(Circles: new ComparableFilter<int>(ComparisonOperator.GreaterThan, 200)),
			null, 0, 100);
		var nonMatching = await _repository.SearchAsync(
			new BeatmapsetSearchFilters(Circles: new ComparableFilter<int>(ComparisonOperator.GreaterThan, 400)),
			null, 0, 100);

		Assert.Contains(matching, set => set[0].Beatmapset.Id == setId);
		Assert.DoesNotContain(nonMatching, set => set[0].Beatmapset.Id == setId);
	}

	[Fact]
	public async Task SearchAsync_CirclesFilter_ExcludesNonStandardModeBeatmaps()
	{
		// TaikoBeatmapObjectCounts has no Circles field, so json_extract(..., '$.Circles') is NULL for
		// it — this must never satisfy a `circles > N` comparison.
		var setId = 5072;
		var mapset = MakeMapset(setId, "TaikoCirclesArtist272");
		await UpsertBeatmapAsync(new Beatmap(new string('s', 32), 272, mapset, "Diff",
			"TaikoCirclesArtist272 - Title (cmyui) [Diff].osu",
			new Difficulty(GameMode.Taiko, 180.0, TimeSpan.FromSeconds(120), 4.0, 9.0, 8.0, 5.0, 3.0),
			new TaikoBeatmapObjectCounts { MaxCombo = 500, Hits = 300 }));

		var results = await _repository.SearchAsync(
			new BeatmapsetSearchFilters(Circles: new ComparableFilter<int>(ComparisonOperator.GreaterThanOrEqual,
				0)),
			null, 0, 100);

		Assert.DoesNotContain(results, set => set[0].Beatmapset.Id == setId);
	}

	[Fact]
	public async Task SearchAsync_CreatorFilter_ExactCaseInsensitiveMatch()
	{
		var setId = 5073;
		var mapset = MakeMapset(setId, creator: "SomeCreator273");
		await UpsertBeatmapAsync(new Beatmap(new string('t', 32), 273, mapset, "Diff",
			"Artist - Title (SomeCreator273) [Diff].osu",
			new Difficulty(GameMode.Standard, 180.0, TimeSpan.FromSeconds(120), 4.0, 9.0, 8.0, 5.0, 3.0),
			new OsuBeatmapObjectCounts { MaxCombo = 500 }));

		var exactMatch = await _repository.SearchAsync(new BeatmapsetSearchFilters(Creator: "SOMECREATOR273"), null,
			0, 100);
		var noMatch = await _repository.SearchAsync(new BeatmapsetSearchFilters(Creator: "Some"), null, 0, 100);

		Assert.Contains(exactMatch, set => set[0].Beatmapset.Id == setId);
		Assert.DoesNotContain(noMatch, set => set[0].Beatmapset.Id == setId);
	}

	[Fact]
	public async Task SearchAsync_StatusFilter_MatchesOnlyTheServersConstantStatus()
	{
		var setId = 5074;
		await UpsertBeatmapAsync(MakeBeatmap(274, setId, new string('u', 32), "StatusFilterArtist274", 1.0));

		var matching = await _repository.SearchAsync(new BeatmapsetSearchFilters(Status: BeatmapStatus.Approved),
			null, 0, 100);
		var nonMatching = await _repository.SearchAsync(new BeatmapsetSearchFilters(Status: BeatmapStatus.Ranked),
			null, 0, 100);

		Assert.Contains(matching, set => set[0].Beatmapset.Id == setId);
		Assert.DoesNotContain(nonMatching, set => set[0].Beatmapset.Id == setId);
	}

	[Fact]
	public async Task SearchAsync_CreatedFilter_YearWindowMatchesWholeYear()
	{
		var setId = 5075;
		var mapset = new Beatmapset(setId, "CreatedFilterArtist275", "Title", "cmyui",
			new DateTime(2019, 6, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2019, 6, 1, 0, 0, 0, DateTimeKind.Utc));
		await UpsertBeatmapAsync(new Beatmap(new string('v', 32), 275, mapset, "Diff",
			"CreatedFilterArtist275 - Title (cmyui) [Diff].osu",
			new Difficulty(GameMode.Standard, 180.0, TimeSpan.FromSeconds(120), 4.0, 9.0, 8.0, 5.0, 3.0),
			new OsuBeatmapObjectCounts { MaxCombo = 500 }));

		var matching = await _repository.SearchAsync(
			new BeatmapsetSearchFilters(Created: new DateFilter(ComparisonOperator.Equal,
				new DateTimeOffset(2019, 1, 1, 0, 0, 0, TimeSpan.Zero),
				new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero))), null, 0, 100);
		var nonMatching = await _repository.SearchAsync(
			new BeatmapsetSearchFilters(Created: new DateFilter(ComparisonOperator.Equal,
				new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero),
				new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero))), null, 0, 100);

		Assert.Contains(matching, set => set[0].Beatmapset.Id == setId);
		Assert.DoesNotContain(nonMatching, set => set[0].Beatmapset.Id == setId);
	}

	[Fact]
	public async Task SearchCountAsync_MatchesSearchAsyncsUnpagedResultCount()
	{
		var setA = 5076;
		var setB = 5077;
		await UpsertBeatmapAsync(MakeBeatmap(276, setA, new string('w', 32), "SearchCountArtist276", 3.0));
		await UpsertBeatmapAsync(MakeBeatmap(277, setB, new string('x', 32), "SearchCountArtist276", 3.0));
		var filters = new BeatmapsetSearchFilters("SearchCountArtist276");

		var count = await _repository.SearchCountAsync(filters, null);
		var page = await _repository.SearchAsync(filters, null, 0, 100);

		Assert.Equal(2, count);
		Assert.Equal(count, page.Count);
	}
}