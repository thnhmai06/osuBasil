using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Multiplayer.Persistence;

/// <summary>A stored row of the <c>round_scores</c> table: the relation between a score and the round it was played in.</summary>
internal sealed class RoundScoreRow
{
	/// <summary>The score submitted in the round.</summary>
	public int ScoreId { get; set; }

	/// <summary>The match the round belongs to.</summary>
	public int MatchId { get; set; }

	/// <summary>The position of the round within its match.</summary>
	public int RoundNumber { get; set; }

	/// <summary>The team the player was on, if the round was a team round.</summary>
	public int? Team { get; set; }
}

internal sealed class RoundScoreRowConfiguration : IEntityTypeConfiguration<RoundScoreRow>
{
	/// <inheritdoc />
	public void Configure(EntityTypeBuilder<RoundScoreRow> builder)
	{
		builder.ToTable("round_scores");
		builder.HasKey(row => row.ScoreId);
		builder.HasOne(ContextModel.ScoresTable, null).WithOne().HasForeignKey(typeof(RoundScoreRow), "ScoreId")
			.OnDelete(DeleteBehavior.NoAction);
		builder.HasIndex(row => new { row.MatchId, row.RoundNumber });
	}
}