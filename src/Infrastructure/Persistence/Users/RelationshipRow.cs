using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Persistence.Users;

/// <summary>A stored row of the <c>relationships</c> table.</summary>
internal sealed class RelationshipRow
{
	public int ActorId { get; set; }
	public int TargetId { get; set; }
	public int Type { get; set; }
	public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class RelationshipRowConfiguration : IEntityTypeConfiguration<RelationshipRow>
{
	public void Configure(EntityTypeBuilder<RelationshipRow> builder)
	{
		builder.ToTable("relationships");
		builder.HasKey(row => new { row.ActorId, row.TargetId });
		builder.HasOne<UserRow>().WithMany().HasForeignKey(row => row.ActorId).OnDelete(DeleteBehavior.NoAction);
		builder.HasOne<UserRow>().WithMany().HasForeignKey(row => row.TargetId).OnDelete(DeleteBehavior.NoAction);
	}
}