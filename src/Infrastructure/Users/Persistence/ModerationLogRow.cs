using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Users.Persistence;

/// <summary>A stored row of the <c>moderation_log</c> table.</summary>
internal sealed class ModerationLogRow
{
	/// <summary>The unique identifier of the entry.</summary>
	public long Id { get; set; }
	/// <summary>The moderation action taken.</summary>
	public int Action { get; set; }
	/// <summary>The user who took the action.</summary>
	public int ById { get; set; }
	/// <summary>The user the action was taken on.</summary>
	public int TargetId { get; set; }
	/// <summary>The reason given, if any.</summary>
	public string? Reason { get; set; }
	/// <summary>When the action was taken.</summary>
	public DateTimeOffset Timestamp { get; set; }
}

internal sealed class ModerationLogRowConfiguration : IEntityTypeConfiguration<ModerationLogRow>
{
	/// <inheritdoc />
	public void Configure(EntityTypeBuilder<ModerationLogRow> builder)
	{
		builder.ToTable("moderation_log");
		builder.HasKey(row => row.Id);
		builder.Property(row => row.Id).ValueGeneratedNever();
		builder.HasOne<UserRow>().WithMany().HasForeignKey(row => row.ById).OnDelete(DeleteBehavior.NoAction);
		builder.HasOne<UserRow>().WithMany().HasForeignKey(row => row.TargetId).OnDelete(DeleteBehavior.NoAction);
		builder.HasIndex(row => new { row.TargetId, row.Timestamp }).IsDescending(false, true);
	}
}