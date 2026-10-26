using Basil.Application.Persistence.Beatmaps;
using Basil.Infrastructure.Persistence.Tests.Support;

namespace Basil.Infrastructure.Persistence.Tests.Beatmaps;

/// <summary>Contract test of a read that overlaps the commit of a deletion: a deleted beatmapset never comes back.</summary>
[Collection("Persistence")]
public sealed class PostgresBeatmapsetRepositoryRemovalRaceTests(PersistenceFixture container)
	: DatabaseTests(container)
{
	[Fact]
	public async Task A_lookup_that_overlaps_a_committed_deletion_does_not_bring_the_beatmapset_back()
	{
		var token = TestContext.Current.CancellationToken;
		await Get<IBeatmapsetRepository>().CommitAsync(
			new BeatmapsetChanges([new BeatmapsetChange(null, StoredBeatmaps.SetData(41))], []), token);
		var gate = new ReadGate();
		await using var server = new StorageComposition(ConnectionString, configure: GatedDatabaseReader.At(gate));
		var sets = server.Get<IBeatmapsetRepository>();

		gate.Arm();
		var overlapping = sets.GetAsync(41, token).AsTask();
		await gate.ReachedAsync(token);
		await sets.DeleteAsync(StoredBeatmaps.Set(41), token);

		// The queued deletion drops its marker in a continuation right after it commits.
		await StoredRows.WaitForAsync(ConnectionString, "select id from beatmapsets where id = @id",
			new Dictionary<string, object?> { ["@id"] = 41 }, rows => rows.Count == 0, "no stored beatmapset", token);
		gate.Release();

		// The first lookup began before the deletion, so either answer is acceptable.
		await overlapping;
		Assert.Null(await sets.GetAsync(41, token));
	}
}