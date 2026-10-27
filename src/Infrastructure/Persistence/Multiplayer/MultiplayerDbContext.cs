using Basil.Common.Infrastructure.Postgres;
using Basil.Infrastructure.Persistence.Scores;
using Basil.Infrastructure.Persistence.Users;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Multiplayer;

internal sealed class MultiplayerDbContext(DbContextOptions<MultiplayerDbContext> options) : DbContext(options),
	IDatabaseContext<MultiplayerDbContext>, IWriteTicketContext
{
	public DbSet<MatchRow> Matches => Set<MatchRow>();
	public DbSet<RoundRow> Rounds => Set<RoundRow>();
	public DbSet<MatchEventRow> MatchEvents => Set<MatchEventRow>();
	public DbSet<RoundScoreRow> RoundScores => Set<RoundScoreRow>();
	public DbSet<WriteTicketRow> WriteTickets => Set<WriteTicketRow>();

	static string IDatabaseContext<MultiplayerDbContext>.HistoryTable => "__ef_migrations_multiplayer";

	static MultiplayerDbContext IDatabaseContext<MultiplayerDbContext>.Create(
		DbContextOptions<MultiplayerDbContext> options) => new(options);

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		UserContextModel.MapUserReference(modelBuilder);
		ScoreContextModel.MapScoreReference(modelBuilder);
		ContextModel.ApplyModuleConfigurations<MultiplayerDbContext>(modelBuilder);
		ContextModel.MapWriteTicketReference(modelBuilder);
	}
}