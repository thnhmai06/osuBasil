using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Multiplayer.Persistence;

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
		ContextModel.MapUsersReference(modelBuilder);
		ContextModel.MapScoresReference(modelBuilder);
		ContextModel.ApplyModuleConfigurations<MultiplayerDbContext>(modelBuilder);
		ContextModel.MapWriteTicketReference(modelBuilder);
	}
}