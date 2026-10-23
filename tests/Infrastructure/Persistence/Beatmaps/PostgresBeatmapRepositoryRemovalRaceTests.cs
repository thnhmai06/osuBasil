using Basil.Application.Persistence.Beatmaps;
using Basil.Application.Persistence.Common;
using Basil.Domain.Beatmaps;
using Basil.Domain.Common;
using Basil.Infrastructure.Persistence.Tests.Support;

namespace Basil.Infrastructure.Persistence.Tests.Beatmaps;

/// <summary>
///     Contract tests of a read that overlaps the commit of a deletion: whatever the read answers, the deleted
///     difficulty never comes back into memory. In each test the first read takes its snapshot before the deletion and
///     delivers its answer after the deletion has committed.
/// </summary>
[Collection("Persistence")]
public sealed class PostgresBeatmapRepositoryRemovalRaceTests : DatabaseTests
{
	private static readonly Md5 Hash = new("d41d8cd98f00b204e9800998ecf8427e");

	public PostgresBeatmapRepositoryRemovalRaceTests(PersistenceFixture container) : base(container) { }

	[Fact]
	public async Task A_hash_lookup_that_overlaps_a_committed_deletion_does_not_bring_the_beatmap_back()
	{
		var token = TestContext.Current.CancellationToken;
		var set = await CommitSetAsync(31);
		var gate = new ReadGate();
		await using var server = new StorageComposition(ConnectionString, configure: GatedDatabaseReader.At(gate));
		var beatmaps = server.Get<IBeatmapRepository>();

		gate.Arm();
		var overlapping = beatmaps.GetAsync(Hash, token).AsTask();
		await gate.ReachedAsync(token);
		await DeleteAllAsync(beatmaps, set, token);
		gate.Release();

		// The first lookup began before the deletion, so either answer is acceptable.
		await overlapping;
		Assert.Null(await beatmaps.GetAsync(Hash, token));
	}

	[Fact]
	public async Task A_query_page_that_overlaps_a_committed_deletion_does_not_bring_the_beatmap_back()
	{
		var token = TestContext.Current.CancellationToken;
		var set = await CommitSetAsync(32);
		var gate = new ReadGate();
		await using var server = new StorageComposition(ConnectionString, configure: GatedDatabaseReader.At(gate));
		var beatmaps = server.Get<IBeatmapRepository>();

		gate.Arm();
		var overlapping = beatmaps.ListAsync(new BeatmapQuery(), new PageRequest(0, 10), token);
		await gate.ReachedAsync(token);
		await DeleteAllAsync(beatmaps, set, token);
		gate.Release();

		// The first page began before the deletion, so it may or may not list the beatmap.
		await overlapping;
		Assert.Null(await beatmaps.GetAsync(Hash, token));
		Assert.Empty((await beatmaps.ListAsync(new BeatmapQuery(), new PageRequest(0, 10), token)).Items);
	}

	private async Task<BeatmapsetRecord> CommitSetAsync(int id)
	{
		await Get<IBeatmapsetRepository>().CommitAsync(
			new BeatmapsetChanges([new BeatmapsetChange(null, StoredBeatmaps.SetData(id))], []),
			TestContext.Current.CancellationToken);
		return StoredBeatmaps.Set(id);
	}

	/// <summary>Deletes every difficulty of a set and returns once the database no longer holds one.</summary>
	private async Task DeleteAllAsync(IBeatmapRepository beatmaps, BeatmapsetRecord set, CancellationToken token)
	{
		await beatmaps.RetainAsync(set, [], token);

		// The queued deletion drops its marker in a continuation right after it commits.
		await StoredRows.WaitForAsync(ConnectionString, "select id from beatmaps where beatmapset_id = @set",
			new Dictionary<string, object?> { ["@set"] = set.Id }, rows => rows.Count == 0,
			"no difficulty of the set", token);
	}
}
