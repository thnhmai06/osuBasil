using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Multiplayer.Persistence;

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
		builder.HasOne(ContextModel.ScoresTable, null).WithOne().HasForeignKey(typeof(RoundScoreRow), "ScoreId")
			.OnDelete(DeleteBehavior.NoAction);
		builder.HasIndex(row => new { row.MatchId, row.RoundNumber });
	}
}