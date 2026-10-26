using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Persistence.Users;

/// <summary>A stored row of the <c>restrictions</c> table.</summary>
internal sealed class RestrictionRow
{
	public int Id { get; set; }
	public int UserId { get; set; }
	public long Permissions { get; set; }
	public DateTimeOffset StartsAt { get; set; }
	public DateTimeOffset? EndsAt { get; set; }
}

internal sealed class RestrictionRowConfiguration : IEntityTypeConfiguration<RestrictionRow>
{
	public void Configure(EntityTypeBuilder<RestrictionRow> builder)
	{
		builder.ToTable("restrictions", table => table.HasCheckConstraint("restrictions_id_check", "id >= 1"));
		builder.HasKey(row => row.Id);
		builder.Property(row => row.Id).UseIdentityByDefaultColumn();
		builder.HasOne<UserRow>().WithMany().HasForeignKey(row => row.UserId);
		builder.HasIndex(row => row.UserId);
	}
}