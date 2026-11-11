using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared;
using Basil.Application.Shared.Exceptions;
using Basil.Application.Shared.Paging;
using Basil.Domain.Beatmaps;
using Basil.Domain.Cryptography;
using Basil.Domain.Mechanics;
using Basil.Domain.Shared;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Beatmaps.Tests.Persistence;

/// <summary>Contract tests of the reading of stored difficulties.</summary>
[Collection("Persistence")]
public sealed class EfBeatmapRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly Md5 BeatmapHash = new("d41d8cd98f00b204e9800998ecf8427e");
	private static readonly Md5 OtherHash = new("11111111111111111111111111111111");
	private static readonly Md5 ThirdHash = new("22222222222222222222222222222222");

	private IBeatmapsetRepository Sets => Get<IBeatmapsetRepository>();

	private IBeatmapRepository Beatmaps => Get<IBeatmapRepository>();

	[Fact]
	public async Task SetListing_RespectsCeiling()
	{
		await CommitAsync(StoredBeatmaps.Data(43, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(null, OtherHash, "Easy")]));
		var set = await Sets.GetAsync(43, TestContext.Current.CancellationToken);
		Assert.NotNull(set);
		var template = (await StoredRows.ReadAsync(ConnectionString,
			"select * from beatmaps where beatmapset_id = @set",
			new Dictionary<string, object?> { ["@set"] = 43 }, TestContext.Current.CancellationToken)).Single();
		await StoredRows.InsertAsync(ConnectionString, "beatmaps", Enumerable.Range(1, ListLimits.Ceiling).Select(g =>
		{
			var copy = new Dictionary<string, object?>(template.Values);
			copy["id"] = 100_000_000 + g;
			copy["hash"] = g.ToString("x32");
			return (IReadOnlyDictionary<string, object?>)copy;
		}), TestContext.Current.CancellationToken);

		var listed = await Beatmaps.ListAsync(set, TestContext.Current.CancellationToken);

		Assert.Equal(ListLimits.Ceiling, listed.Count);
	}

	[Fact]
	public async Task RetainedDeletion_StoredOnReturn()
	{
		var sets = Get<IBeatmapsetRepository>();
		var beatmaps = Get<IBeatmapRepository>();
		var set = StoredBeatmaps.Set(11);
		await sets.CommitAsync(new BeatmapsetChanges(
			[new BeatmapsetChange(null, StoredBeatmaps.SetData(11))], []));
		Assert.NotNull(await beatmaps.GetAsync(BeatmapHash, TestContext.Current.CancellationToken));

		await beatmaps.RetainAsync(set, []);

		var rows = await StoredRows.ReadAsync(ConnectionString, "select id from beatmaps where beatmapset_id = @set",
			new Dictionary<string, object?> { ["@set"] = 11 }, TestContext.Current.CancellationToken);
		Assert.Empty(rows);
		Assert.Null(await beatmaps.GetAsync(BeatmapHash, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task OsuIdlessDifficulty_LocalId()
	{
		await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(null, OtherHash, "Easy")]));
		var set = await Sets.GetAsync(42, TestContext.Current.CancellationToken);
		Assert.NotNull(set);

		var listed = await Beatmaps.ListAsync(set, TestContext.Current.CancellationToken);

		Assert.True(Assert.Single(listed).Id >= 1_000_000_000);
	}

	[Fact]
	public async Task ById_FindsDifficulty()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));

		var found = await Beatmaps.GetAsync(301, TestContext.Current.CancellationToken);

		Assert.NotNull(found);
		Assert.Equal(301, found.Id);
	}

	[Fact]
	public async Task ByHash_FindsDifficulty()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));

		var found = await Beatmaps.GetAsync(OtherHash, TestContext.Current.CancellationToken);

		Assert.NotNull(found);
		Assert.Equal(301, found.Id);
	}

	[Fact]
	public async Task SetDifficulties_StoredOrder()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Title", "Mapper",
		[
			StoredBeatmaps.Difficulty(303, ThirdHash, "Third"),
			StoredBeatmaps.Difficulty(301, OtherHash, "First"),
			StoredBeatmaps.Difficulty(302, BeatmapHash, "Second")
		]));
		var set = await Sets.GetAsync(11, TestContext.Current.CancellationToken);
		Assert.NotNull(set);

		var listed = await Beatmaps.ListAsync(set, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { 301, 302, 303 }, listed.Select(beatmap => beatmap.Id));
	}

	[Fact]
	public async Task ArtistText_FindsDifficulty()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Alpha", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));
		await CommitAsync(StoredBeatmaps.Data(22, "Nanahira", "Beta", "Mapper",
			[StoredBeatmaps.Difficulty(302, BeatmapHash, "Easy")]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery("Camellia"), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(301, Assert.Single(page.Items).Id);
	}

	[Fact]
	public async Task TitleText_FindsDifficulty()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Alpha", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));
		await CommitAsync(StoredBeatmaps.Data(22, "Camellia", "Beta", "Mapper",
			[StoredBeatmaps.Difficulty(302, BeatmapHash, "Easy")]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery("Alpha"), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(301, Assert.Single(page.Items).Id);
	}

	[Fact]
	public async Task MapperText_FindsDifficulty()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Alpha", "FirstMapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));
		await CommitAsync(StoredBeatmaps.Data(22, "Camellia", "Beta", "SecondMapper",
			[StoredBeatmaps.Difficulty(302, BeatmapHash, "Easy")]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery("SecondMapper"), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(302, Assert.Single(page.Items).Id);
	}

	[Fact]
	public async Task NameText_FindsDifficulty()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Alpha", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Insane")]));
		await CommitAsync(StoredBeatmaps.Data(22, "Camellia", "Beta", "Mapper",
			[StoredBeatmaps.Difficulty(302, BeatmapHash, "Extra")]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery("Insane"), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(301, Assert.Single(page.Items).Id);
	}

	[Fact]
	public async Task TextCase_Ignored()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Alpha", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));
		await CommitAsync(StoredBeatmaps.Data(22, "Nanahira", "Beta", "Mapper",
			[StoredBeatmaps.Difficulty(302, BeatmapHash, "Easy")]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery("cAMELLIA"), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(301, Assert.Single(page.Items).Id);
	}

	[Fact]
	public async Task PercentInText_Literal()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "100%", "Alpha", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));
		await CommitAsync(StoredBeatmaps.Data(22, "1000", "Beta", "Mapper",
			[StoredBeatmaps.Difficulty(302, BeatmapHash, "Easy")]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery("100%"), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(301, Assert.Single(page.Items).Id);
	}

	[Fact]
	public async Task UnderscoreInText_Literal()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "a_b", "Alpha", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));
		await CommitAsync(StoredBeatmaps.Data(22, "axb", "Beta", "Mapper",
			[StoredBeatmaps.Difficulty(302, BeatmapHash, "Easy")]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery("a_b"), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(301, Assert.Single(page.Items).Id);
	}

	[Theory]
	[InlineData(100, 1)]
	[InlineData(200, 1)]
	[InlineData(99, 0)]
	[InlineData(201, 0)]
	public async Task CircleCountRange_FiltersDifficulties(int circles, int expected)
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy", circles: circles)]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(Circles: new Interval<int>(100, 200)),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(expected, page.Items.Count);
	}

	[Theory]
	[InlineData(120, 1)]
	[InlineData(240, 1)]
	[InlineData(119, 0)]
	[InlineData(241, 0)]
	public async Task LengthRange_FiltersDifficulties(int seconds, int expected)
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy", lengthSeconds: seconds)]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(Length: new Interval<double>(120, 240)),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(expected, page.Items.Count);
	}

	[Theory]
	[InlineData(4, 1)]
	[InlineData(5, 1)]
	[InlineData(3.9, 0)]
	[InlineData(5.1, 0)]
	public async Task CircleSizeRange_FiltersDifficulties(double cs, int expected)
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy", cs: cs)]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(Cs: new Interval<double>(4, 5)),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(expected, page.Items.Count);
	}

	[Theory]
	[InlineData(9, 1)]
	[InlineData(10, 1)]
	[InlineData(8.9, 0)]
	[InlineData(10.1, 0)]
	public async Task ApproachRateRange_FiltersDifficulties(double ar, int expected)
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy", ar: ar)]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(Ar: new Interval<double>(9, 10)),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(expected, page.Items.Count);
	}

	[Theory]
	[InlineData(8, 1)]
	[InlineData(9, 1)]
	[InlineData(7.9, 0)]
	[InlineData(9.1, 0)]
	public async Task OverallDifficultyRange_Filters(double od, int expected)
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy", od: od)]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(Od: new Interval<double>(8, 9)),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(expected, page.Items.Count);
	}

	[Theory]
	[InlineData(5, 1)]
	[InlineData(6, 1)]
	[InlineData(4.9, 0)]
	[InlineData(6.1, 0)]
	public async Task HealthDrainRange_FiltersDifficulties(double hp, int expected)
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy", hp: hp)]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(Hp: new Interval<double>(5, 6)),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(expected, page.Items.Count);
	}

	[Fact]
	public async Task ByMode_FindsDifficulty()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Alpha", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));
		await CommitAsync(StoredBeatmaps.Data(22, "Camellia", "Beta", "Mapper",
			[StoredBeatmaps.Difficulty(302, BeatmapHash, "Hard", GameMode.Taiko)]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(Mode: GameMode.Taiko), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(302, Assert.Single(page.Items).Id);
	}

	[Fact]
	public async Task RankedSet_FindsDifficulties()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Alpha", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));
		await CommitAsync(StoredBeatmaps.Data(22, "Camellia", "Beta", "Mapper",
			[StoredBeatmaps.Difficulty(302, BeatmapHash, "Hard")]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(Status: BeatmapsetStatus.Ranked),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(2, page.Items.Count);
	}

	[Fact]
	public async Task OtherStatus_LeavesEveryDifficulty()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Alpha", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(Status: BeatmapsetStatus.Approved),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Empty(page.Items);
	}

	[Fact]
	public async Task FileName_FindsDifficulty()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Alpha", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));
		await CommitAsync(StoredBeatmaps.Data(22, "Camellia", "Beta", "Mapper",
			[StoredBeatmaps.Difficulty(302, BeatmapHash, "Hard")]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(FileName: "Camellia - Alpha (Mapper) [Easy].osu"),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(301, Assert.Single(page.Items).Id);
	}

	[Fact]
	public async Task FileNameCase_Finds()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Alpha", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(FileName: "CAMELLIA - alpha (mapper) [EASY].OSU"),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(301, Assert.Single(page.Items).Id);
	}

	[Fact]
	public async Task UnsafeNameCharacters_StillFound()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "A:B/C", "100%_Sure*?", "Map|per",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Hard <1>")]));
		await CommitAsync(StoredBeatmaps.Data(22, "ABC", "100xxSure", "Mapper",
			[StoredBeatmaps.Difficulty(302, BeatmapHash, "Hard 1")]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(FileName: "ABC - 100%_Sure (Mapper) [Hard 1].osu"),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(301, Assert.Single(page.Items).Id);
	}

	[Fact]
	public async Task Listing_ReturnsRequestedPart()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Alpha", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));
		await CommitAsync(StoredBeatmaps.Data(22, "Camellia", "Beta", "Mapper",
			[StoredBeatmaps.Difficulty(302, BeatmapHash, "Hard")]));
		await CommitAsync(StoredBeatmaps.Data(33, "Camellia", "Gamma", "Mapper",
			[StoredBeatmaps.Difficulty(303, ThirdHash, "Extra")]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(), new PageRequest(0, 2),
			TestContext.Current.CancellationToken);

		Assert.True(page.HasNext);
		Assert.Equal(2, page.Items.Count);
	}

	[Fact]
	public async Task DuplicateDifficultyHash_Refused()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Alpha", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));
		await CommitAsync(StoredBeatmaps.Data(22, "Camellia", "Beta", "Mapper",
			[StoredBeatmaps.Difficulty(302, BeatmapHash, "Hard")]));
		var beatmap = await Beatmaps.GetAsync(301, TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<AlreadyExistsException>(() =>
			Beatmaps.UpdateAsync(Changed(beatmap!, BeatmapHash)));
	}

	[Fact]
	public async Task StoredDifficultyHash_Refused()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Alpha", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));
		await using (var other = new StorageComposition(ConnectionString))
		{
			await other.Get<IBeatmapsetRepository>().CommitAsync(new BeatmapsetChanges(
			[
				new BeatmapsetChange(null, StoredBeatmaps.Data(22, "Camellia", "Beta", "Mapper",
					[StoredBeatmaps.Difficulty(302, BeatmapHash, "Hard")]))
			], []));
		}

		var beatmap = await Beatmaps.GetAsync(301, TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<AlreadyExistsException>(() =>
			Beatmaps.UpdateAsync(Changed(beatmap!, BeatmapHash)));
	}

	/// <summary>Commits one beatmapset as a new set.</summary>
	/// <param name="data">The set as it is stored.</param>
	private async Task CommitAsync(BeatmapsetData data)
	{
		await Sets.CommitAsync(new BeatmapsetChanges([new BeatmapsetChange(null, data)], []));
	}

	/// <summary>Copies a difficulty with another hash.</summary>
	/// <param name="beatmap">The difficulty to copy.</param>
	/// <param name="hash">The hash the copy has.</param>
	/// <returns>The copy.</returns>
	private static BeatmapRecord Changed(BeatmapRecord beatmap, Md5 hash)
	{
		return new BeatmapRecord
		{
			Id = beatmap.Id,
			Beatmapset = beatmap.Beatmapset,
			Hash = hash,
			Metadata = beatmap.Metadata,
			Assets = beatmap.Assets,
			Locked = beatmap.Locked,
			Visible = beatmap.Visible,
			CreatedAt = beatmap.CreatedAt,
			UpdatedAt = beatmap.UpdatedAt
		};
	}
}