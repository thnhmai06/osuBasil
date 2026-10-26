using Basil.Infrastructure.Persistence.Scores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Persistence.Multiplayer;

/// <summary>A stored row of the <c>round_scores</c> table: the relation between a score and the round it was played in.</summary>
internal sealed class RoundScoreRow
{
	public int ScoreId { get; set; }
	public int MatchId { get; set; }
	public int RoundNumber { get; set; }
	public int? Team { get; set; }
}

internal sealed class RoundScoreRowConfiguration : IEntityTypeConfiguration<RoundScoreRow>
{
	public void Configure(EntityTypeBuilder<RoundScoreRow> builder)
	{
		builder.ToTable("round_scores");
		builder.HasKey(row => row.ScoreId);
		builder.HasOne<ScoreRow>().WithOne().HasForeignKey<RoundScoreRow>(row => row.ScoreId)
			.OnDelete(DeleteBehavior.NoAction);
		builder.HasIndex(row => new { row.MatchId, row.RoundNumber });
	}
}