using Basil.Infrastructure.Persistence.Auth;
using Basil.Infrastructure.Persistence.Beatmaps;
using Basil.Infrastructure.Persistence.Chat;
using Basil.Infrastructure.Persistence.Content;
using Basil.Infrastructure.Persistence.Multiplayer;
using Basil.Infrastructure.Persistence.Scores;
using Basil.Infrastructure.Persistence.Users;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Postgres;

/// <summary>
///     The shape of the server's database. It is short-lived: one is made for each read or write and never tracks what
///     it loads, because memory, not the context, is where the server's data lives.
/// </summary>
internal sealed class BasilDbContext(DbContextOptions<BasilDbContext> options) : DbContext(options)
{
	/// <summary>The first id of the beatmapsets and beatmaps created on this server.</summary>
	internal const int LocalIdFloor = 1_000_000_000;

	public DbSet<UserRow> Users => Set<UserRow>();
	public DbSet<CredentialRow> Credentials => Set<CredentialRow>();
	public DbSet<LoginRow> Logins => Set<LoginRow>();
	public DbSet<RestrictionRow> Restrictions => Set<RestrictionRow>();
	public DbSet<ModerationLogRow> ModerationLog => Set<ModerationLogRow>();
	public DbSet<RelationshipRow> Relationships => Set<RelationshipRow>();
	public DbSet<ChannelRow> Channels => Set<ChannelRow>();
	public DbSet<SettingRow> Settings => Set<SettingRow>();
	public DbSet<MenuBannerRow> MenuBanners => Set<MenuBannerRow>();
	public DbSet<BeatmapsetRow> Beatmapsets => Set<BeatmapsetRow>();
	public DbSet<BeatmapRow> Beatmaps => Set<BeatmapRow>();
	public DbSet<MatchRow> Matches => Set<MatchRow>();
	public DbSet<RoundRow> Rounds => Set<RoundRow>();
	public DbSet<MatchEventRow> MatchEvents => Set<MatchEventRow>();
	public DbSet<ScoreRow> Scores => Set<ScoreRow>();
	public DbSet<RoundScoreRow> RoundScores => Set<RoundScoreRow>();
	public DbSet<UserStatsRow> UserStats => Set<UserStatsRow>();
	public DbSet<WriteTicketRow> WriteTickets => Set<WriteTicketRow>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.ApplyConfigurationsFromAssembly(typeof(BasilDbContext).Assembly);
		modelBuilder.HasSequence<int>("local_beatmapset_ids").StartsAt(LocalIdFloor);
		modelBuilder.HasSequence<int>("local_beatmap_ids").StartsAt(LocalIdFloor);
	}
}