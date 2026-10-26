using Basil.Infrastructure.Persistence.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Persistence.Scores;

/// <summary>A stored row of the <c>user_stats</c> table.</summary>
internal sealed class UserStatsRow
{
	public int UserId { get; set; }
	public int Mode { get; set; }
	public long TotalScore { get; set; }
	public long RankedScore { get; set; }
	public int PlayCount { get; set; }
}

internal sealed class UserStatsRowConfiguration : IEntityTypeConfiguration<UserStatsRow>
{
	public void Configure(EntityTypeBuilder<UserStatsRow> builder)
	{
		builder.ToTable("user_stats");
		builder.HasKey(row => new { row.UserId, row.Mode });
		builder.HasOne<UserRow>().WithMany().HasForeignKey(row => row.UserId).OnDelete(DeleteBehavior.NoAction);
	}
}