using Basil.Infrastructure.Common.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Scores.Persistence;

internal sealed class ScoresDbContext(DbContextOptions<ScoresDbContext> options) : DbContext(options),
	IDatabaseContext<ScoresDbContext>, IWriteTicketContext
{
	public DbSet<ScoreRow> Scores => Set<ScoreRow>();
	public DbSet<UserStatsRow> UserStats => Set<UserStatsRow>();
	public DbSet<WriteTicketRow> WriteTickets => Set<WriteTicketRow>();

	static string IDatabaseContext<ScoresDbContext>.HistoryTable => "__ef_migrations_scores";

	static ScoresDbContext IDatabaseContext<ScoresDbContext>.Create(DbContextOptions<ScoresDbContext> options) =>
		new(options);

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		ContextModel.MapUsersReference(modelBuilder);
		ContextModel.ApplyModuleConfigurations<ScoresDbContext>(modelBuilder);
		ContextModel.MapWriteTicketReference(modelBuilder);
	}
}