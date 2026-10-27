using Basil.Application.Persistence.Beatmaps;
using Basil.Application.Persistence.Common;
using Basil.Domain.Beatmaps;
using Basil.Domain.Common;
using Basil.Infrastructure.Persistence.Beatmaps;
using Basil.Infrastructure.Persistence.Postgres;
using Basil.Infrastructure.Persistence.Tests.Support;
using Npgsql;

namespace Basil.Infrastructure.Persistence.Tests.Beatmaps;

/// <summary>Contract tests of the storing of beatmapsets.</summary>
[Collection("Persistence")]
public sealed class PostgresBeatmapsetRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly Md5 EasyHash = new("11111111111111111111111111111111");
	private static readonly Md5 HardHash = new("22222222222222222222222222222222");
	private static readonly Md5 ThirdHash = new("33333333333333333333333333333333");

	private IBeatmapsetRepository Sets => Get<IBeatmapsetRepository>();

	[Fact]
	public async Task A_committed_set_keeps_the_osu_id_it_was_committed_with()
	{
		var stored = await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper"));

		Assert.Equal(42, Assert.Single(stored).Id);
	}

	[Fact]
	public async Task A_set_without_an_osu_id_is_given_a_local_id()
	{
		var stored = await CommitAsync(StoredBeatmaps.Data(null, "Camellia", "Title", "Mapper"));

		Assert.True(Assert.Single(stored).Id >= 1_000_000_000);
	}

	[Fact]
	public async Task Sets_without_an_osu_id_are_given_increasing_local_ids()
	{
		var first = await CommitAsync(StoredBeatmaps.Data(null, "Camellia", "First", "Mapper"));
		var second = await CommitAsync(StoredBeatmaps.Data(null, "Camellia", "Second", "Mapper"));

		Assert.True(Assert.Single(second).Id > Assert.Single(first).Id);
	}

	[Fact]
	public async Task Committed_sets_are_given_in_the_order_they_were_committed()
	{
		var stored = await Sets.CommitAsync(new BeatmapsetChanges(
		[
			new BeatmapsetChange(null, StoredBeatmaps.Data(11, "Camellia", "First", "Mapper")),
			new BeatmapsetChange(null, StoredBeatmaps.Data(22, "Camellia", "Second", "Mapper"))
		], []), TestContext.Current.CancellationToken);

		Assert.Equal(new[] { 11, 22 }, stored.Select(set => set.Id));
	}

	[Fact]
	public async Task A_replaced_set_takes_the_new_metadata()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper")));

		var replaced = await Sets.CommitAsync(new BeatmapsetChanges(
				[new BeatmapsetChange(set, StoredBeatmaps.Data(42, "Camellia", "Renamed", "Mapper"))], []),
			TestContext.Current.CancellationToken);

		Assert.Equal("Renamed", Assert.Single(replaced).Metadata.Title);
	}

	[Fact]
	public async Task A_replaced_set_keeps_the_moment_it_was_created()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper")));

		var replaced = await Sets.CommitAsync(new BeatmapsetChanges(
				[new BeatmapsetChange(set, StoredBeatmaps.Data(42, "Camellia", "Renamed", "Mapper"))], []),
			TestContext.Current.CancellationToken);

		Assert.Equal(set.CreatedAt, Assert.Single(replaced).CreatedAt);
	}

	[Fact]
	public async Task A_replaced_set_keeps_its_lock()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper")));
		set.Locked = true;

		var replaced = await Sets.CommitAsync(new BeatmapsetChanges(
				[new BeatmapsetChange(set, StoredBeatmaps.Data(42, "Camellia", "Renamed", "Mapper"))], []),
			TestContext.Current.CancellationToken);

		Assert.True(Assert.Single(replaced).Locked);
	}

	[Fact]
	public async Task A_replaced_set_keeps_its_visibility()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper")));
		set.Visible = false;

		var replaced = await Sets.CommitAsync(new BeatmapsetChanges(
				[new BeatmapsetChange(set, StoredBeatmaps.Data(42, "Camellia", "Renamed", "Mapper"))], []),
			TestContext.Current.CancellationToken);

		Assert.False(Assert.Single(replaced).Visible);
	}

	[Fact]
	public async Task A_difficulty_the_set_no_longer_has_is_deleted()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, EasyHash, "Easy"), StoredBeatmaps.Difficulty(302, HardHash, "Hard")])));

		await Sets.CommitAsync(new BeatmapsetChanges(
		[
			new BeatmapsetChange(set, StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper",
				[StoredBeatmaps.Difficulty(301, EasyHash, "Easy")]))
		], []), TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select id from beatmaps where beatmapset_id = @id",
			new Dictionary<string, object?> { ["@id"] = set.Id }, rows => rows.Count == 1,
			"the one difficulty the set still has", TestContext.Current.CancellationToken);

		Assert.Single(rows);
	}

	[Fact]
	public async Task A_removed_set_is_not_found_any_more()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper")));

		await Sets.CommitAsync(new BeatmapsetChanges([], [set]), TestContext.Current.CancellationToken);

		Assert.Null(await Sets.GetAsync(set.Id, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_removed_set_is_deleted_with_its_difficulties()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, EasyHash, "Easy")])));

		await Sets.CommitAsync(new BeatmapsetChanges([], [set]), TestContext.Current.CancellationToken);

		var sets = await StoredRows.WaitForAsync(ConnectionString, "select id from beatmapsets where id = @id",
			new Dictionary<string, object?> { ["@id"] = set.Id }, rows => rows.Count == 0,
			"the deletion of the set", TestContext.Current.CancellationToken);
		var beatmaps = await StoredRows.WaitForAsync(ConnectionString,
			"select id from beatmaps where beatmapset_id = @id",
			new Dictionary<string, object?> { ["@id"] = set.Id }, rows => rows.Count == 0,
			"the deletion of the difficulties", TestContext.Current.CancellationToken);

		Assert.Empty(sets);
		Assert.Empty(beatmaps);
	}

	[Fact]
	public async Task A_new_set_that_has_the_id_another_stored_set_has_is_refused()
	{
		await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "First", "Mapper"));

		await Assert.ThrowsAsync<AlreadyExistsException>(() =>
			CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Second", "Mapper")));
	}

	[Fact]
	public async Task A_new_set_whose_difficulty_has_the_hash_another_stored_difficulty_has_is_refused()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "First", "Mapper",
			[StoredBeatmaps.Difficulty(301, EasyHash, "Easy")]));

		await Assert.ThrowsAsync<AlreadyExistsException>(() =>
			CommitAsync(StoredBeatmaps.Data(22, "Camellia", "Second", "Mapper",
				[StoredBeatmaps.Difficulty(302, EasyHash, "Hard")])));
	}

	[Fact]
	public async Task A_committed_set_stores_its_difficulties()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, EasyHash, "Easy")])));

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select id from beatmaps where beatmapset_id = @id",
			new Dictionary<string, object?> { ["@id"] = set.Id }, rows => rows.Count == 1,
			"the stored difficulty of the set", TestContext.Current.CancellationToken);

		Assert.Single(rows);
	}

	[Fact]
	public async Task A_new_difficulty_that_has_the_id_another_stored_difficulty_has_is_refused()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "First", "Mapper",
			[StoredBeatmaps.Difficulty(301, EasyHash, "Easy")]));

		await Assert.ThrowsAsync<AlreadyExistsException>(() =>
			CommitAsync(StoredBeatmaps.Data(22, "Camellia", "Second", "Mapper",
				[StoredBeatmaps.Difficulty(301, HardHash, "Hard")])));
	}

	[Fact]
	public async Task A_committed_set_is_the_set_the_store_returns()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper")));

		var found = await Sets.GetAsync(set.Id, TestContext.Current.CancellationToken);

		Assert.Same(set, found);
	}

	[Fact]
	public async Task An_update_changes_the_lock_of_the_set_the_store_returns()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper")));
		var changed = Changed(set, true);

		await Sets.UpdateAsync(changed, TestContext.Current.CancellationToken);

		var found = await Sets.GetAsync(set.Id, TestContext.Current.CancellationToken);
		Assert.True(found!.Locked);
	}

	[Fact]
	public async Task An_update_changes_the_visibility_of_the_set_the_store_returns()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper")));
		var changed = Changed(set, visible: false);

		await Sets.UpdateAsync(changed, TestContext.Current.CancellationToken);

		var found = await Sets.GetAsync(set.Id, TestContext.Current.CancellationToken);
		Assert.False(found!.Visible);
	}

	[Fact]
	public async Task An_update_is_stored()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper")));
		await Sets.UpdateAsync(Changed(set, true, false), TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select locked, visible from beatmapsets where id = @id",
			new Dictionary<string, object?> { ["@id"] = set.Id },
			rows => rows.Count == 1 && rows[0].Bool("locked") && !rows[0].Bool("visible"),
			"the stored lock and visibility of the set", TestContext.Current.CancellationToken);

		Assert.Single(rows);
	}

	[Fact]
	public async Task A_deleted_set_is_not_found_at_once()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper")));

		await Sets.DeleteAsync(set, TestContext.Current.CancellationToken);

		Assert.Null(await Sets.GetAsync(set.Id, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_deleted_set_is_removed_from_the_table()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper")));

		await Sets.DeleteAsync(set, TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select id from beatmapsets where id = @id",
			new Dictionary<string, object?> { ["@id"] = set.Id }, rows => rows.Count == 0,
			"the deletion of the set", TestContext.Current.CancellationToken);

		Assert.Empty(rows);
	}

	[Fact]
	public async Task A_set_whose_deletion_is_queued_is_not_listed()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, EasyHash, "Easy")])));
		await Sets.DeleteAsync(set, TestContext.Current.CancellationToken);

		var page = await Sets.ListAsync(new BeatmapQuery(), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Empty(page.Items);
	}

	[Fact]
	public async Task A_listing_finds_the_set_one_of_whose_difficulties_matches()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "First", "Mapper",
			[StoredBeatmaps.Difficulty(301, EasyHash, "Easy")]));
		await CommitAsync(StoredBeatmaps.Data(22, "Camellia", "Second", "Mapper",
			[StoredBeatmaps.Difficulty(302, HardHash, "Hard")]));

		var page = await Sets.ListAsync(new BeatmapQuery("Hard"), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(22, Assert.Single(page.Items).Id);
	}

	[Fact]
	public async Task A_listing_shows_the_newest_set_first()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "First", "Mapper",
			[StoredBeatmaps.Difficulty(301, EasyHash, "Easy")]));
		await CommitAsync(StoredBeatmaps.Data(22, "Camellia", "Second", "Mapper",
			[StoredBeatmaps.Difficulty(302, HardHash, "Hard")]));

		var page = await Sets.ListAsync(new BeatmapQuery(), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Equal(new[] { 22, 11 }, page.Items.Select(set => set.Id));
	}

	[Fact]
	public async Task A_listing_returns_the_requested_part()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "First", "Mapper",
			[StoredBeatmaps.Difficulty(301, EasyHash, "Easy")]));
		await CommitAsync(StoredBeatmaps.Data(22, "Camellia", "Second", "Mapper",
			[StoredBeatmaps.Difficulty(302, HardHash, "Hard")]));
		await CommitAsync(StoredBeatmaps.Data(33, "Camellia", "Third", "Mapper",
			[StoredBeatmaps.Difficulty(303, ThirdHash, "Extra")]));

		var page = await Sets.ListAsync(new BeatmapQuery(), new PageRequest(1, 1),
			TestContext.Current.CancellationToken);

		Assert.Equal(3, page.Total);
		Assert.Equal(22, Assert.Single(page.Items).Id);
	}

	[Fact]
	public async Task A_commit_is_stored_after_the_writes_queued_before_it_for_the_set_it_replaces()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, EasyHash, "Easy")])));
		var beatmaps = Get<IBeatmapRepository>();
		var easy = (await beatmaps.GetAsync(EasyHash, TestContext.Current.CancellationToken))!;
		Task commit;

		await using (var blocker = new NpgsqlConnection(ConnectionString))
		{
			await blocker.OpenAsync(TestContext.Current.CancellationToken);
			await using var transaction = await blocker.BeginTransactionAsync(TestContext.Current.CancellationToken);
			await new NpgsqlCommand("lock table beatmapsets in exclusive mode", blocker, transaction)
				.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

			// The update of the set waits for the lock and the update of the difficulty waits behind it, so both are
			// still unstored when the commit is asked for.
			set.Locked = true;
			await Sets.UpdateAsync(set, TestContext.Current.CancellationToken);
			await beatmaps.UpdateAsync(easy, TestContext.Current.CancellationToken);
			commit = Sets.CommitAsync(new BeatmapsetChanges(
			[
				new BeatmapsetChange(set, StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper",
					[StoredBeatmaps.Difficulty(301, EasyHash, "Easy renamed")]))
			], []), TestContext.Current.CancellationToken);

			// A commit that is stored before the writes queued earlier would be overwritten by them afterwards.
			await Assert.ThrowsAsync<TimeoutException>(() =>
				commit.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
			await transaction.CommitAsync(TestContext.Current.CancellationToken);
		}

		await commit;
		await Get<DatabaseWriter>().WriteAsync<BeatmapsDbContext>(Root.Beatmapset(42),
			(_, _) => Task.CompletedTask,
			TestContext.Current.CancellationToken);
		var rows = await StoredRows.ReadAsync(ConnectionString, "select version from beatmaps where hash = @hash",
			new Dictionary<string, object?> { ["@hash"] = EasyHash.HashValue }, TestContext.Current.CancellationToken);
		Assert.Equal("Easy renamed", Assert.Single(rows).Text("version"));
	}

	/// <summary>Commits one beatmapset as a new set.</summary>
	/// <param name="data">The set as it is stored.</param>
	/// <returns>The stored set.</returns>
	private async Task<IReadOnlyList<BeatmapsetRecord>> CommitAsync(BeatmapsetData data)
	{
		return await Sets.CommitAsync(new BeatmapsetChanges([new BeatmapsetChange(null, data)], []),
			TestContext.Current.CancellationToken);
	}

	/// <summary>Copies a beatmapset with the lock and the visibility the test names.</summary>
	/// <param name="set">The set to copy.</param>
	/// <param name="locked">Whether the copy is write-locked.</param>
	/// <param name="visible">Whether the copy is shown.</param>
	/// <returns>The copy.</returns>
	private static BeatmapsetRecord Changed(BeatmapsetRecord set, bool locked = false, bool visible = true)
	{
		return new BeatmapsetRecord
		{
			Id = set.Id,
			Fingerprint = set.Fingerprint,
			Metadata = set.Metadata,
			Assets = set.Assets,
			CreatedAt = set.CreatedAt,
			UpdatedAt = set.UpdatedAt,
			Locked = locked,
			Visible = visible
		};
	}
}