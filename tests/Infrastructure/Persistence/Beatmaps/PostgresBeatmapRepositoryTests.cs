using Basil.Application.Persistence.Beatmaps;
using Basil.Domain.Common;
using Basil.Infrastructure.Persistence.Tests.Support;
using Npgsql;

namespace Basil.Infrastructure.Persistence.Tests.Beatmaps;

/// <summary>Contract tests of the reading of stored difficulties.</summary>
public sealed class PostgresBeatmapRepositoryTests : DatabaseTests, IClassFixture<PersistenceFixture>
{
	private static readonly Md5 BeatmapHash = new("d41d8cd98f00b204e9800998ecf8427e");

	public PostgresBeatmapRepositoryTests(PersistenceFixture container) : base(container) { }

	[Fact]
	public async Task A_deletion_queued_makes_the_hash_lookup_answer_nothing()
	{
		var sets = Get<IBeatmapsetRepository>();
		var beatmaps = Get<IBeatmapRepository>();
		var set = StoredBeatmaps.Set(11);
		await sets.CommitAsync(new BeatmapsetChanges(
			[new BeatmapsetChange(null, StoredBeatmaps.SetData(11))], []));
		Assert.NotNull(await beatmaps.GetAsync(BeatmapHash));

		await using (var connection = new NpgsqlConnection(ConnectionString))
		{
			await connection.OpenAsync();
			await using var transaction = await connection.BeginTransactionAsync();

			// Holding an exclusive lock keeps the queued deletion blocked, so the row is still stored while the
			// deletion waits for its commit.
			await new NpgsqlCommand("lock table beatmaps in exclusive mode", connection, transaction)
				.ExecuteNonQueryAsync();
			try
			{
				await beatmaps.RetainAsync(set, [], TestContext.Current.CancellationToken);
				Assert.Null(await beatmaps.GetAsync(BeatmapHash));
			}
			finally
			{
				await transaction.CommitAsync();
			}
		}

		Assert.Null(await beatmaps.GetAsync(BeatmapHash));
	}
}
