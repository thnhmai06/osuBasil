using Basil.Infrastructure.Persistence.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Persistence.Multiplayer;

/// <summary>A stored row of the <c>match_events</c> table.</summary>
internal sealed class MatchEventRow
{
	public long Id { get; set; }
	public int MatchId { get; set; }
	public int Type { get; set; }
	public DateTimeOffset Timestamp { get; set; }
	public int? ActorId { get; set; }
	public int? TargetId { get; set; }
	public string? Detail { get; set; }
}

internal sealed class MatchEventRowConfiguration : IEntityTypeConfiguration<MatchEventRow>
{
	public void Configure(EntityTypeBuilder<MatchEventRow> builder)
	{
		builder.ToTable("match_events");
		builder.HasKey(row => row.Id);
		builder.Property(row => row.Id).UseIdentityAlwaysColumn();
		builder.HasOne<MatchRow>().WithMany().HasForeignKey(row => row.MatchId).OnDelete(DeleteBehavior.NoAction);
		builder.HasOne<UserRow>().WithMany().HasForeignKey(row => row.ActorId).OnDelete(DeleteBehavior.NoAction);
		builder.HasOne<UserRow>().WithMany().HasForeignKey(row => row.TargetId).OnDelete(DeleteBehavior.NoAction);
		builder.HasIndex(row => row.MatchId);
	}
}
