using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Beatmaps.Persistence;

internal sealed class BeatmapsDbContext(DbContextOptions<BeatmapsDbContext> options) : DbContext(options),
	IDatabaseContext<BeatmapsDbContext>
{
	internal const int LocalIdFloor = 1_000_000_000;
	internal const string LocalBeatmapsetIds = "local_beatmapset";
	internal const string LocalBeatmapIds = "local_beatmap";

	/// <summary>The stored beatmapsets.</summary>
	public DbSet<BeatmapsetRow> Beatmapsets => Set<BeatmapsetRow>();
	/// <summary>The stored beatmaps.</summary>
	public DbSet<BeatmapRow> Beatmaps => Set<BeatmapRow>();

	static string IDatabaseContext<BeatmapsDbContext>.HistoryTable => "__ef_migrations_beatmaps";

	static BeatmapsDbContext IDatabaseContext<BeatmapsDbContext>.Create(DbContextOptions<BeatmapsDbContext> options)
	{
		return new BeatmapsDbContext(options);
	}

	/// <inheritdoc />
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		ContextModel.ApplyModuleConfigurations<BeatmapsDbContext>(modelBuilder);
		IdCounters.Map(modelBuilder, "beatmap_id_counters", (LocalBeatmapsetIds, LocalIdFloor),
			(LocalBeatmapIds, LocalIdFloor));
	}
}