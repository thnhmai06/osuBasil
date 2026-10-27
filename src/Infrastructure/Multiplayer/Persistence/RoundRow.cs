using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Multiplayer.Persistence;

/// <summary>A stored row of the <c>rounds</c> table.</summary>
internal sealed class RoundRow
{
	public int MatchId { get; set; }
	public int Number { get; set; }
	public string BeatmapHash { get; set; } = "";
	public int Mode { get; set; }
	public int Mods { get; set; }
	public bool Freemods { get; set; }
	public int TeamType { get; set; }
	public int WinCondition { get; set; }
	public int Seed { get; set; }
	public DateTimeOffset StartedAt { get; set; }
	public DateTimeOffset? EndedAt { get; set; }
	public bool Aborted { get; set; }

	/// <summary>What was decided for the round, as JSON, or null while it is not decided.</summary>
	public string? Result { get; set; }
}

internal sealed class RoundRowConfiguration : IEntityTypeConfiguration<RoundRow>
{
	public void Configure(EntityTypeBuilder<RoundRow> builder)
	{
		builder.ToTable("rounds");
		builder.HasKey(row => new { row.MatchId, row.Number });
		builder.Property(row => row.Result).HasColumnType("jsonb");
		builder.HasOne<MatchRow>().WithMany().HasForeignKey(row => row.MatchId).OnDelete(DeleteBehavior.NoAction);
	}
}