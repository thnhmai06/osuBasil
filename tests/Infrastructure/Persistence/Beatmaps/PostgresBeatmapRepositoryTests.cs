using Basil.Application.Persistence.Beatmaps;
using Basil.Application.Persistence.Common;
using Basil.Domain.Beatmaps;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Infrastructure.Persistence.Tests.Support;
using Npgsql;

namespace Basil.Infrastructure.Persistence.Tests.Beatmaps;

/// <summary>Contract tests of the reading of stored difficulties.</summary>
[Collection("Persistence")]
public sealed class PostgresBeatmapRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly Md5 BeatmapHash = new("d41d8cd98f00b204e9800998ecf8427e");
	private static readonly Md5 OtherHash = new("11111111111111111111111111111111");
	private static readonly Md5 ThirdHash = new("22222222222222222222222222222222");

	private IBeatmapsetRepository Sets => Get<IBeatmapsetRepository>();

	private IBeatmapRepository Beatmaps => Get<IBeatmapRepository>();

	[Fact]
	public async Task A_deletion_queued_makes_the_hash_lookup_answer_nothing()
	{
		var sets = Get<IBeatmapsetRepository>();
		var beatmaps = Get<IBeatmapRepository>();
		var set = StoredBeatmaps.Set(11);
		await sets.CommitAsync(new BeatmapsetChanges(
			[new BeatmapsetChange(null, StoredBeatmaps.SetData(11))], []), TestContext.Current.CancellationToken);
		Assert.NotNull(await beatmaps.GetAsync(BeatmapHash, TestContext.Current.CancellationToken));

		await using (var connection = new NpgsqlConnection(ConnectionString))
		{
			await connection.OpenAsync(TestContext.Current.CancellationToken);
			await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

			// Holding an exclusive lock keeps the queued deletion blocked, so the row is still stored while the
			// deletion waits for its commit.
			await new NpgsqlCommand("lock table beatmaps in exclusive mode", connection, transaction)
				.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
			try
			{
				await beatmaps.RetainAsync(set, [], TestContext.Current.CancellationToken);
				Assert.Null(await beatmaps.GetAsync(BeatmapHash, TestContext.Current.CancellationToken));
			}
			finally
			{
				await transaction.CommitAsync(TestContext.Current.CancellationToken);
			}
		}

		Assert.Null(await beatmaps.GetAsync(BeatmapHash, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_difficulty_without_an_osu_id_is_given_a_local_id()
	{
		await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(null, OtherHash, "Easy")]));
		var set = await Sets.GetAsync(42, TestContext.Current.CancellationToken);
		Assert.NotNull(set);

		var listed = await Beatmaps.ListAsync(set, TestContext.Current.CancellationToken);

		Assert.True(Assert.Single(listed).Id >= 1_000_000_000);
	}

	[Fact]
	public async Task A_difficulty_is_found_by_its_id()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));

		var found = await Beatmaps.GetAsync(301, TestContext.Current.CancellationToken);

		Assert.NotNull(found);
		Assert.Equal(301, found.Id);
	}

	[Fact]
	public async Task A_difficulty_is_found_by_its_hash()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));

		var found = await Beatmaps.GetAsync(OtherHash, TestContext.Current.CancellationToken);

		Assert.NotNull(found);
		Assert.Equal(301, found.Id);
	}

	[Fact]
	public async Task The_difficulties_of_a_set_are_listed_in_the_order_they_are_stored()
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
	public async Task A_listing_finds_a_difficulty_by_the_text_of_its_artist()
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
	public async Task A_listing_finds_a_difficulty_by_the_text_of_its_title()
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
	public async Task A_listing_finds_a_difficulty_by_the_text_of_its_mapper()
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
	public async Task A_listing_finds_a_difficulty_by_the_text_of_its_name()
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
	public async Task A_listing_ignores_the_case_of_the_text()
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
	public async Task A_listing_takes_a_percent_in_the_text_literally()
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
	public async Task A_listing_takes_an_underscore_in_the_text_literally()
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
	public async Task A_listing_holds_the_difficulties_whose_circle_count_is_in_the_range(int circles, int expected)
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy", circles: circles)]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(Circles: new Interval<int>(100, 200)),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(expected, page.Total);
	}

	[Theory]
	[InlineData(120, 1)]
	[InlineData(240, 1)]
	[InlineData(119, 0)]
	[InlineData(241, 0)]
	public async Task A_listing_holds_the_difficulties_whose_length_is_in_the_range(int seconds, int expected)
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy", lengthSeconds: seconds)]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(Length: new Interval<double>(120, 240)),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(expected, page.Total);
	}

	[Theory]
	[InlineData(4, 1)]
	[InlineData(5, 1)]
	[InlineData(3.9, 0)]
	[InlineData(5.1, 0)]
	public async Task A_listing_holds_the_difficulties_whose_circle_size_is_in_the_range(double cs, int expected)
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy", cs: cs)]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(Cs: new Interval<double>(4, 5)),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(expected, page.Total);
	}

	[Theory]
	[InlineData(9, 1)]
	[InlineData(10, 1)]
	[InlineData(8.9, 0)]
	[InlineData(10.1, 0)]
	public async Task A_listing_holds_the_difficulties_whose_approach_rate_is_in_the_range(double ar, int expected)
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy", ar: ar)]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(Ar: new Interval<double>(9, 10)),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(expected, page.Total);
	}

	[Theory]
	[InlineData(8, 1)]
	[InlineData(9, 1)]
	[InlineData(7.9, 0)]
	[InlineData(9.1, 0)]
	public async Task A_listing_holds_the_difficulties_whose_overall_difficulty_is_in_the_range(double od, int expected)
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy", od: od)]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(Od: new Interval<double>(8, 9)),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(expected, page.Total);
	}

	[Theory]
	[InlineData(5, 1)]
	[InlineData(6, 1)]
	[InlineData(4.9, 0)]
	[InlineData(6.1, 0)]
	public async Task A_listing_holds_the_difficulties_whose_health_drain_is_in_the_range(double hp, int expected)
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy", hp: hp)]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(Hp: new Interval<double>(5, 6)),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(expected, page.Total);
	}

	[Fact]
	public async Task A_listing_finds_a_difficulty_by_its_mode()
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
	public async Task A_listing_finds_the_difficulties_of_an_approved_set()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Alpha", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));
		await CommitAsync(StoredBeatmaps.Data(22, "Camellia", "Beta", "Mapper",
			[StoredBeatmaps.Difficulty(302, BeatmapHash, "Hard")]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(Status: BeatmapsetStatus.Approved),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(2, page.Total);
	}

	[Fact]
	public async Task A_listing_leaves_out_every_difficulty_when_another_status_is_asked_for()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Alpha", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(Status: BeatmapsetStatus.Ranked),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(0, page.Total);
	}

	[Fact]
	public async Task A_listing_finds_a_difficulty_by_its_file_name()
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
	public async Task A_listing_finds_a_difficulty_by_its_file_name_whatever_the_letter_case()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Alpha", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(FileName: "CAMELLIA - alpha (mapper) [EASY].OSU"),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(301, Assert.Single(page.Items).Id);
	}

	[Fact]
	public async Task A_listing_finds_a_difficulty_whose_names_hold_characters_a_file_name_cannot_hold()
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
	public async Task A_listing_returns_the_requested_part()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Alpha", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));
		await CommitAsync(StoredBeatmaps.Data(22, "Camellia", "Beta", "Mapper",
			[StoredBeatmaps.Difficulty(302, BeatmapHash, "Hard")]));
		await CommitAsync(StoredBeatmaps.Data(33, "Camellia", "Gamma", "Mapper",
			[StoredBeatmaps.Difficulty(303, ThirdHash, "Extra")]));

		var page = await Beatmaps.ListAsync(new BeatmapQuery(), new PageRequest(0, 2),
			TestContext.Current.CancellationToken);

		Assert.Equal(3, page.Total);
		Assert.Equal(2, page.Items.Count);
	}

	[Fact]
	public async Task A_difficulty_whose_hash_another_difficulty_has_is_refused()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Alpha", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));
		await CommitAsync(StoredBeatmaps.Data(22, "Camellia", "Beta", "Mapper",
			[StoredBeatmaps.Difficulty(302, BeatmapHash, "Hard")]));
		var beatmap = await Beatmaps.GetAsync(301, TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<AlreadyExistsException>(() =>
			Beatmaps.UpdateAsync(Changed(beatmap!, BeatmapHash), TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_difficulty_whose_hash_a_stored_difficulty_has_is_refused()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "Alpha", "Mapper",
			[StoredBeatmaps.Difficulty(301, OtherHash, "Easy")]));
		await using (var other = new StorageComposition(ConnectionString))
		{
			await other.Get<IBeatmapsetRepository>().CommitAsync(new BeatmapsetChanges(
			[
				new BeatmapsetChange(null, StoredBeatmaps.Data(22, "Camellia", "Beta", "Mapper",
					[StoredBeatmaps.Difficulty(302, BeatmapHash, "Hard")]))
			], []), TestContext.Current.CancellationToken);
		}

		var beatmap = await Beatmaps.GetAsync(301, TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<AlreadyExistsException>(() =>
			Beatmaps.UpdateAsync(Changed(beatmap!, BeatmapHash), TestContext.Current.CancellationToken));
	}

	/// <summary>Commits one beatmapset as a new set.</summary>
	/// <param name="data">The set as it is stored.</param>
	private async Task CommitAsync(BeatmapsetData data)
	{
		await Sets.CommitAsync(new BeatmapsetChanges([new BeatmapsetChange(null, data)], []),
			TestContext.Current.CancellationToken);
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