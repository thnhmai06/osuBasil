using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Multiplayer.Persistence;

/// <summary>A stored row of the <c>matches</c> table.</summary>
internal sealed class MatchRow
{
	public int Id { get; set; }
	public string Name { get; set; } = "";
	public int? CreatorId { get; set; }
	public DateTimeOffset StartedAt { get; set; }
	public DateTimeOffset? EndedAt { get; set; }
	public bool IsPrivate { get; set; }
}

internal sealed class MatchRowConfiguration : IEntityTypeConfiguration<MatchRow>
{
	public void Configure(EntityTypeBuilder<MatchRow> builder)
	{
		builder.ToTable("matches", table => table.HasCheckConstraint("matches_id_check", "id >= 1"));
		builder.HasKey(row => row.Id);
		builder.Property(row => row.Id).UseIdentityByDefaultColumn();
		builder.HasOne(ContextModel.UsersTable, (string?)null).WithMany().HasForeignKey("CreatorId").OnDelete(DeleteBehavior.NoAction);
		builder.HasIndex(row => row.EndedAt);
	}
}