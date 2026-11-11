using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Scores.Persistence;

/// <summary>The persisted per-mode statistics of one player.</summary>
internal sealed class UserStatsRow
{
	/// <summary>The player the statistics belong to.</summary>
	public int UserId { get; set; }
	/// <summary>The game mode the statistics cover.</summary>
	public int Mode { get; set; }
	/// <summary>The cumulative score across every submitted play in the mode.</summary>
	public long TotalScore { get; set; }
	/// <summary>The cumulative score across every submitted play on ranked beatmaps in the mode.</summary>
	public long RankedScore { get; set; }
	/// <summary>The number of plays submitted in the mode.</summary>
	public int PlayCount { get; set; }
}

internal sealed class UserStatsRowConfiguration : IEntityTypeConfiguration<UserStatsRow>
{
	/// <inheritdoc />
	public void Configure(EntityTypeBuilder<UserStatsRow> builder)
	{
		builder.ToTable("user_stats");
		builder.HasKey(row => new { row.UserId, row.Mode });
		builder.HasOne(ContextModel.UsersTable, null).WithMany().HasForeignKey("UserId")
			.OnDelete(DeleteBehavior.NoAction);
	}
}