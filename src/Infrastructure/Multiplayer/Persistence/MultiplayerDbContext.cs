using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Multiplayer.Persistence;

internal sealed class MultiplayerDbContext(DbContextOptions<MultiplayerDbContext> options) : DbContext(options),
	IDatabaseContext<MultiplayerDbContext>
{
	/// <summary>The stored matches.</summary>
	public DbSet<MatchRow> Matches => Set<MatchRow>();
	/// <summary>The stored rounds.</summary>
	public DbSet<RoundRow> Rounds => Set<RoundRow>();
	/// <summary>The stored match events.</summary>
	public DbSet<MatchEventRow> MatchEvents => Set<MatchEventRow>();
	/// <summary>The stored round scores.</summary>
	public DbSet<RoundScoreRow> RoundScores => Set<RoundScoreRow>();

	static string IDatabaseContext<MultiplayerDbContext>.HistoryTable => "__ef_migrations_multiplayer";

	static MultiplayerDbContext IDatabaseContext<MultiplayerDbContext>.Create(
		DbContextOptions<MultiplayerDbContext> options)
	{
		return new MultiplayerDbContext(options);
	}

	/// <inheritdoc />
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		IdCounters.Map(modelBuilder, "multiplayer_id_counters", ("match", 1), ("match_event", 1));
		ContextModel.MapUsersReference(modelBuilder);
		ContextModel.MapScoresReference(modelBuilder);
		ContextModel.ApplyModuleConfigurations<MultiplayerDbContext>(modelBuilder);
	}
}