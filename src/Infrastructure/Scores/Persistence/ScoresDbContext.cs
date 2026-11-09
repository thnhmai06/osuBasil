using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Scores.Persistence;

internal sealed class ScoresDbContext(DbContextOptions<ScoresDbContext> options) : DbContext(options),
	IDatabaseContext<ScoresDbContext>
{
	public DbSet<ScoreRow> Scores => Set<ScoreRow>();
	public DbSet<UserStatsRow> UserStats => Set<UserStatsRow>();

	static string IDatabaseContext<ScoresDbContext>.HistoryTable => "__ef_migrations_scores";

	static ScoresDbContext IDatabaseContext<ScoresDbContext>.Create(DbContextOptions<ScoresDbContext> options)
	{
		return new ScoresDbContext(options);
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		IdCounters.Map(modelBuilder, "score_id_counters", ("score", 1));
		ContextModel.MapUsersReference(modelBuilder);
		ContextModel.ApplyModuleConfigurations<ScoresDbContext>(modelBuilder);
	}
}