using Basil.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Beatmaps;

internal sealed class BeatmapsDbContext(DbContextOptions<BeatmapsDbContext> options) : DbContext(options),
	IDatabaseContext<BeatmapsDbContext>, IWriteTicketContext
{
	internal const int LocalIdFloor = 1_000_000_000;

	public DbSet<BeatmapsetRow> Beatmapsets => Set<BeatmapsetRow>();
	public DbSet<BeatmapRow> Beatmaps => Set<BeatmapRow>();
	public DbSet<WriteTicketRow> WriteTickets => Set<WriteTicketRow>();

	static string IDatabaseContext<BeatmapsDbContext>.HistoryTable => "__ef_migrations_beatmaps";

	static BeatmapsDbContext IDatabaseContext<BeatmapsDbContext>.Create(DbContextOptions<BeatmapsDbContext> options) =>
		new(options);

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		ContextModel.ApplyModuleConfigurations<BeatmapsDbContext>(modelBuilder);
		ContextModel.MapWriteTicketReference(modelBuilder);
		modelBuilder.HasSequence<int>("local_beatmapset_ids").StartsAt(LocalIdFloor);
		modelBuilder.HasSequence<int>("local_beatmap_ids").StartsAt(LocalIdFloor);
	}
}