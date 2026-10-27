using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Users.Persistence;

/// <summary>A stored row of the <c>moderation_log</c> table.</summary>
internal sealed class ModerationLogRow
{
	public long Id { get; set; }
	public int Action { get; set; }
	public int ById { get; set; }
	public int TargetId { get; set; }
	public string? Reason { get; set; }
	public DateTimeOffset Timestamp { get; set; }
}

internal sealed class ModerationLogRowConfiguration : IEntityTypeConfiguration<ModerationLogRow>
{
	public void Configure(EntityTypeBuilder<ModerationLogRow> builder)
	{
		builder.ToTable("moderation_log");
		builder.HasKey(row => row.Id);
		builder.Property(row => row.Id).UseIdentityAlwaysColumn();
		builder.HasOne<UserRow>().WithMany().HasForeignKey(row => row.ById).OnDelete(DeleteBehavior.NoAction);
		builder.HasOne<UserRow>().WithMany().HasForeignKey(row => row.TargetId).OnDelete(DeleteBehavior.NoAction);
		builder.HasIndex(row => new { row.TargetId, row.Timestamp }).IsDescending(false, true);
	}
}