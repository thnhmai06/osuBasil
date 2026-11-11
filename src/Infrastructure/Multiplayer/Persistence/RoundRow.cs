using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Multiplayer.Persistence;

/// <summary>A stored row of the <c>rounds</c> table.</summary>
internal sealed class RoundRow
{
	/// <summary>The match the round belongs to.</summary>
	public int MatchId { get; set; }

	/// <summary>The position of the round within its match.</summary>
	public int Number { get; set; }

	/// <summary>The MD5 hash of the beatmap that was played.</summary>
	public string BeatmapHash { get; set; } = "";

	/// <summary>The game mode of the round.</summary>
	public int Mode { get; set; }

	/// <summary>The mods of the round, as a bit set.</summary>
	public int Mods { get; set; }

	/// <summary>Whether players could pick their own mods.</summary>
	public bool Freemods { get; set; }

	/// <summary>The team type of the round.</summary>
	public int TeamType { get; set; }

	/// <summary>The win condition of the round.</summary>
	public int WinCondition { get; set; }

	/// <summary>The random seed of the round.</summary>
	public int Seed { get; set; }

	/// <summary>When the round started.</summary>
	public DateTimeOffset StartedAt { get; set; }

	/// <summary>When the round ended, or <see langword="null" /> while it lasts.</summary>
	public DateTimeOffset? EndedAt { get; set; }

	/// <summary>Whether the round was aborted.</summary>
	public bool Aborted { get; set; }

	/// <summary>What was decided for the round, as JSON, or null while it is not decided.</summary>
	public string? Result { get; set; }
}

internal sealed class RoundRowConfiguration : IEntityTypeConfiguration<RoundRow>
{
	/// <inheritdoc />
	public void Configure(EntityTypeBuilder<RoundRow> builder)
	{
		builder.ToTable("rounds");
		builder.HasKey(row => new { row.MatchId, row.Number });
		builder.HasOne<MatchRow>().WithMany().HasForeignKey(row => row.MatchId).OnDelete(DeleteBehavior.NoAction);
	}
}