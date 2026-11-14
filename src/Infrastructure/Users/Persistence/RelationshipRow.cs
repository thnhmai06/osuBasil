using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Users.Persistence;

/// <summary>A stored row of the <c>relationships</c> table.</summary>
internal sealed class RelationshipRow
{
	/// <summary>The user who holds the relationship.</summary>
	public int ActorId { get; set; }

	/// <summary>The user the relationship points at.</summary>
	public int TargetId { get; set; }

	/// <summary>The kind of relationship.</summary>
	public int Type { get; set; }

	/// <summary>When the relationship was created.</summary>
	public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class RelationshipRowConfiguration : IEntityTypeConfiguration<RelationshipRow>
{
	/// <inheritdoc />
	public void Configure(EntityTypeBuilder<RelationshipRow> builder)
	{
		builder.ToTable("relationships");
		builder.HasKey(row => new { row.ActorId, row.TargetId });
		builder.HasOne<UserRow>().WithMany().HasForeignKey(row => row.ActorId).OnDelete(DeleteBehavior.NoAction);
		builder.HasOne<UserRow>().WithMany().HasForeignKey(row => row.TargetId).OnDelete(DeleteBehavior.NoAction);
	}
}