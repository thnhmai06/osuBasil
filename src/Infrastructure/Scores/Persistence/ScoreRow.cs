using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Scores.Persistence;

/// <summary>The persisted form of a submitted score.</summary>
internal sealed class ScoreRow
{
	/// <summary>The unique identifier of the score.</summary>
	public int Id { get; set; }
	/// <summary>The player who set the score, or <see langword="null" /> when that account no longer exists.</summary>
	public int? UserId { get; set; }
	/// <summary>The MD5 hash of the beatmap the score was set on.</summary>
	public string? BeatmapHash { get; set; }
	/// <summary>The game mode the score was set in.</summary>
	public int Mode { get; set; }
	/// <summary>The mods that were active, as a bit set.</summary>
	public int Mods { get; set; }
	/// <summary>The number of 300 hits.</summary>
	public int Num300 { get; set; }
	/// <summary>The number of 100 hits.</summary>
	public int Num100 { get; set; }
	/// <summary>The number of 50 hits.</summary>
	public int Num50 { get; set; }
	/// <summary>The number of geki hits.</summary>
	public int NumGeki { get; set; }
	/// <summary>The number of katu hits.</summary>
	public int NumKatu { get; set; }
	/// <summary>The number of misses.</summary>
	public int NumMiss { get; set; }
	/// <summary>The total score points.</summary>
	public int TotalScore { get; set; }
	/// <summary>The highest combo reached.</summary>
	public int MaxCombo { get; set; }
	/// <summary>The combo running when the play ended.</summary>
	public int CurrentCombo { get; set; }
	/// <summary>The grade awarded to the play.</summary>
	public int Grade { get; set; }
	/// <summary>Whether the play was passed rather than failed.</summary>
	public bool IsPassed { get; set; }
	/// <summary>Whether no combo was broken during the play.</summary>
	public bool IsFullCombo { get; set; }
	/// <summary>When the score was set.</summary>
	public DateTimeOffset Timestamp { get; set; }
	/// <summary>The submission checksum that identifies the play and prevents it from being stored twice.</summary>
	public string? Checksum { get; set; }
}

internal sealed class ScoreRowConfiguration : IEntityTypeConfiguration<ScoreRow>
{
	/// <inheritdoc />
	public void Configure(EntityTypeBuilder<ScoreRow> builder)
	{
		builder.ToTable("scores", table => table.HasCheckConstraint("scores_id_check", "id >= 1"));
		builder.HasKey(row => row.Id);
		builder.Property(row => row.Id).ValueGeneratedNever();
		builder.HasOne(ContextModel.UsersTable, null).WithMany().HasForeignKey("UserId")
			.OnDelete(DeleteBehavior.NoAction);
		builder.Property(row => row.BeatmapHash).HasMaxLength(128);
		builder.Property(row => row.Checksum).HasMaxLength(128);
		builder.HasIndex(row => row.UserId);
		// The leaderboard of a beatmap: best score first, then the earlier play and the lower id (also serves a lookup by beatmap).
		builder.HasIndex(row => new { row.BeatmapHash, row.TotalScore, row.Timestamp, row.Id })
			.IsDescending(false, true, false, false);
		builder.HasIndex(row => row.Checksum).IsUnique();
	}
}