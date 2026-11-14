using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared.Exceptions;
using Basil.Application.Shared.Paging;
using Basil.Domain.Beatmaps;
using Basil.Domain.Cryptography;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Beatmaps.Tests.Persistence;

/// <summary>Contract tests of the storing of beatmapsets.</summary>
[Collection("Persistence")]
public sealed class EfBeatmapsetRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly Md5 EasyHash = new("11111111111111111111111111111111");
	private static readonly Md5 HardHash = new("22222222222222222222222222222222");
	private static readonly Md5 ThirdHash = new("33333333333333333333333333333333");

	private IBeatmapsetRepository Sets => Get<IBeatmapsetRepository>();

	[Fact]
	public async Task CommittedSet_KeepsOsuId()
	{
		var stored = await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper"));

		Assert.Equal(42, Assert.Single(stored).Id);
	}

	[Fact]
	public async Task OsuIdlessSet_LocalId()
	{
		var stored = await CommitAsync(StoredBeatmaps.Data(null, "Camellia", "Title", "Mapper"));

		Assert.True(Assert.Single(stored).Id >= 1_000_000_000);
	}

	[Fact]
	public async Task OsuIdlessSets_IncreasingIds()
	{
		var first = await CommitAsync(StoredBeatmaps.Data(null, "Camellia", "First", "Mapper"));
		var second = await CommitAsync(StoredBeatmaps.Data(null, "Camellia", "Second", "Mapper"));

		Assert.True(Assert.Single(second).Id > Assert.Single(first).Id);
	}

	[Fact]
	public async Task CommittedSets_GivenInOrder()
	{
		var stored = await Sets.CommitAsync(new BeatmapsetChanges(
		[
			new BeatmapsetChange(null, StoredBeatmaps.Data(11, "Camellia", "First", "Mapper")),
			new BeatmapsetChange(null, StoredBeatmaps.Data(22, "Camellia", "Second", "Mapper"))
		], []));

		Assert.Equal(new[] { 11, 22 }, stored.Select(set => set.Id));
	}

	[Fact]
	public async Task ReplacedSet_TakesNewMetadata()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper")));

		var replaced = await Sets.CommitAsync(new BeatmapsetChanges(
			[new BeatmapsetChange(set, StoredBeatmaps.Data(42, "Camellia", "Renamed", "Mapper"))], []));

		Assert.Equal("Renamed", Assert.Single(replaced).Metadata.Title);
	}

	[Fact]
	public async Task ReplacedSet_KeepsCreationMoment()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper")));

		var replaced = await Sets.CommitAsync(new BeatmapsetChanges(
			[new BeatmapsetChange(set, StoredBeatmaps.Data(42, "Camellia", "Renamed", "Mapper"))], []));

		Assert.Equal(set.CreatedAt, Assert.Single(replaced).CreatedAt);
	}

	[Fact]
	public async Task ReplacedSet_KeepsLock()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper")));
		set.Locked = true;

		var replaced = await Sets.CommitAsync(new BeatmapsetChanges(
			[new BeatmapsetChange(set, StoredBeatmaps.Data(42, "Camellia", "Renamed", "Mapper"))], []));

		Assert.True(Assert.Single(replaced).Locked);
	}

	[Fact]
	public async Task ReplacedSet_KeepsVisibility()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper")));
		set.Visible = false;

		var replaced = await Sets.CommitAsync(new BeatmapsetChanges(
			[new BeatmapsetChange(set, StoredBeatmaps.Data(42, "Camellia", "Renamed", "Mapper"))], []));

		Assert.False(Assert.Single(replaced).Visible);
	}

	[Fact]
	public async Task DroppedDifficulty_Deleted()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, EasyHash, "Easy"), StoredBeatmaps.Difficulty(302, HardHash, "Hard")])));

		await Sets.CommitAsync(new BeatmapsetChanges(
		[
			new BeatmapsetChange(set, StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper",
				[StoredBeatmaps.Difficulty(301, EasyHash, "Easy")]))
		], []));

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select id from beatmaps where beatmapset_id = @id",
			new Dictionary<string, object?> { ["@id"] = set.Id }, rows => rows.Count == 1,
			"the one difficulty the set still has", TestContext.Current.CancellationToken);

		Assert.Single(rows);
	}

	[Fact]
	public async Task RemovedSet_NotFound()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper")));

		await Sets.CommitAsync(new BeatmapsetChanges([], [set]));

		Assert.Null(await Sets.GetAsync(set.Id, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task RemovedSet_DeletedWithDifficulties()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, EasyHash, "Easy")])));

		await Sets.CommitAsync(new BeatmapsetChanges([], [set]));

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
	public async Task DuplicateSetId_Refused()
	{
		await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "First", "Mapper"));

		await Assert.ThrowsAsync<AlreadyExistsException>(() =>
			CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Second", "Mapper")));
	}

	[Fact]
	public async Task NewSetHash_Refused()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "First", "Mapper",
			[StoredBeatmaps.Difficulty(301, EasyHash, "Easy")]));

		await Assert.ThrowsAsync<AlreadyExistsException>(() =>
			CommitAsync(StoredBeatmaps.Data(22, "Camellia", "Second", "Mapper",
				[StoredBeatmaps.Difficulty(302, EasyHash, "Hard")])));
	}

	[Fact]
	public async Task CommittedSet_StoresDifficulties()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, EasyHash, "Easy")])));

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select id from beatmaps where beatmapset_id = @id",
			new Dictionary<string, object?> { ["@id"] = set.Id }, rows => rows.Count == 1,
			"the stored difficulty of the set", TestContext.Current.CancellationToken);

		Assert.Single(rows);
	}

	[Fact]
	public async Task DuplicateDifficultyId_Refused()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "First", "Mapper",
			[StoredBeatmaps.Difficulty(301, EasyHash, "Easy")]));

		await Assert.ThrowsAsync<AlreadyExistsException>(() =>
			CommitAsync(StoredBeatmaps.Data(22, "Camellia", "Second", "Mapper",
				[StoredBeatmaps.Difficulty(301, HardHash, "Hard")])));
	}

	[Fact]
	public async Task CommittedSet_FoundById()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper")));

		var found = await Sets.GetAsync(set.Id, TestContext.Current.CancellationToken);

		Assert.Equal(set, found);
	}

	[Fact]
	public async Task Update_ReturnedLockChanged()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper")));
		var changed = Changed(set, true);

		await Sets.UpdateAsync(changed);

		var found = await Sets.GetAsync(set.Id, TestContext.Current.CancellationToken);
		Assert.True(found!.Locked);
	}

	[Fact]
	public async Task Update_ChangesReturnedVisibility()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper")));
		var changed = Changed(set, visible: false);

		await Sets.UpdateAsync(changed);

		var found = await Sets.GetAsync(set.Id, TestContext.Current.CancellationToken);
		Assert.False(found!.Visible);
	}

	[Fact]
	public async Task Update_Stored()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper")));
		await Sets.UpdateAsync(Changed(set, true, false));

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select locked, visible from beatmapsets where id = @id",
			new Dictionary<string, object?> { ["@id"] = set.Id },
			rows => rows.Count == 1 && rows[0].Bool("locked") && !rows[0].Bool("visible"),
			"the stored lock and visibility of the set", TestContext.Current.CancellationToken);

		Assert.Single(rows);
	}

	[Fact]
	public async Task DeletedSet_ImmediatelyGone()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper")));

		await Sets.DeleteAsync(set);

		Assert.Null(await Sets.GetAsync(set.Id, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task DeletedSet_RemovedFromTable()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper")));

		await Sets.DeleteAsync(set);

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select id from beatmapsets where id = @id",
			new Dictionary<string, object?> { ["@id"] = set.Id }, rows => rows.Count == 0,
			"the deletion of the set", TestContext.Current.CancellationToken);

		Assert.Empty(rows);
	}

	[Fact]
	public async Task QueuedDeletion_NotListed()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, EasyHash, "Easy")])));
		await Sets.DeleteAsync(set);

		var page = await Sets.ListAsync(new BeatmapQuery(), new PageRequest(0, 10),
			TestContext.Current.CancellationToken);

		Assert.Empty(page.Items);
	}

	[Fact]
	public async Task MatchingDifficulty_FindsSet()
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
	public async Task Listing_NewestSetFirst()
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
	public async Task Listing_ReturnsRequestedPart()
	{
		await CommitAsync(StoredBeatmaps.Data(11, "Camellia", "First", "Mapper",
			[StoredBeatmaps.Difficulty(301, EasyHash, "Easy")]));
		await CommitAsync(StoredBeatmaps.Data(22, "Camellia", "Second", "Mapper",
			[StoredBeatmaps.Difficulty(302, HardHash, "Hard")]));
		await CommitAsync(StoredBeatmaps.Data(33, "Camellia", "Third", "Mapper",
			[StoredBeatmaps.Difficulty(303, ThirdHash, "Extra")]));

		var page = await Sets.ListAsync(new BeatmapQuery(), new PageRequest(1, 1),
			TestContext.Current.CancellationToken);

		Assert.True(page.HasNext);
		Assert.Equal(22, Assert.Single(page.Items).Id);
	}

	[Fact]
	public async Task CommitAfterUpdate_ReplacesUpdate()
	{
		var set = Assert.Single(await CommitAsync(StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper",
			[StoredBeatmaps.Difficulty(301, EasyHash, "Easy")])));
		var beatmaps = Get<IBeatmapRepository>();
		var easy = (await beatmaps.GetAsync(EasyHash, TestContext.Current.CancellationToken))!;
		set.Locked = true;
		await Sets.UpdateAsync(set);
		await beatmaps.UpdateAsync(easy);

		await Sets.CommitAsync(new BeatmapsetChanges(
		[
			new BeatmapsetChange(set, StoredBeatmaps.Data(42, "Camellia", "Title", "Mapper",
				[StoredBeatmaps.Difficulty(301, EasyHash, "Easy renamed")]))
		], []));

		var rows = await StoredRows.ReadAsync(ConnectionString, "select version from beatmaps where hash = @hash",
			new Dictionary<string, object?> { ["@hash"] = EasyHash.HashValue }, TestContext.Current.CancellationToken);
		Assert.Equal("Easy renamed", Assert.Single(rows).Text("version"));
	}

	[Fact]
	public async Task RefusedBeatmapUpdate_ReachesCaller()
	{
		// The beatmap belongs to a set that is not stored, which breaks the foreign key.
		var orphan = StoredBeatmaps.Beatmap(777);

		await Assert.ThrowsAnyAsync<Exception>(() =>
			Get<IBeatmapRepository>().UpdateAsync(orphan));
	}

	/// <summary>Commits one beatmapset as a new set.</summary>
	/// <param name="data">The set as it is stored.</param>
	/// <returns>The stored set.</returns>
	private async Task<IReadOnlyList<BeatmapsetRecord>> CommitAsync(BeatmapsetData data)
	{
		return await Sets.CommitAsync(new BeatmapsetChanges([new BeatmapsetChange(null, data)], []));
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