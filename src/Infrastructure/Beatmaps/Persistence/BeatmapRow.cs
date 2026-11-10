using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Beatmaps.Persistence;

/// <summary>
///     A stored row of the <c>beatmaps</c> table. <see cref="Objects" /> is the JSON of a <c>BeatmapObjects</c> value;
///     <see cref="Length" /> and <see cref="PreviewTime" /> are in milliseconds.
/// </summary>
internal sealed class BeatmapRow
{
	/// <summary>The unique identifier of the beatmap.</summary>
	public int Id { get; set; }
	/// <summary>The beatmapset the beatmap belongs to.</summary>
	public int BeatmapsetId { get; set; }
	/// <summary>The MD5 hash of the beatmap file.</summary>
	public string Hash { get; set; } = "";
	/// <summary>The difficulty name.</summary>
	public string Version { get; set; } = "";
	/// <summary>The game mode the beatmap is played in.</summary>
	public int Mode { get; set; }
	/// <summary>The displayed star rating.</summary>
	public double Star { get; set; }
	/// <summary>The playable length, in milliseconds.</summary>
	public int Length { get; set; }
	/// <summary>The beats per minute.</summary>
	public double Bpm { get; set; }
	/// <summary>The circle size.</summary>
	public double Cs { get; set; }
	/// <summary>The approach rate.</summary>
	public double Ar { get; set; }
	/// <summary>The overall difficulty.</summary>
	public double Od { get; set; }
	/// <summary>The HP drain rate.</summary>
	public double Hp { get; set; }
	/// <summary>The hit objects counted by kind.</summary>
	public JsonElement Objects { get; set; }

	/// <summary>
	///     The number of circles in <see cref="Objects" /> (standard beatmaps only), kept as a column so that a listing
	///     can filter by it.
	/// </summary>
	public int? Circles { get; set; }

	/// <summary>
	///     The number of sliders in <see cref="Objects" /> (standard beatmaps only), kept as a column so that a listing
	///     can filter by it.
	/// </summary>
	public int? Sliders { get; set; }

	/// <summary>The background image file name, if any.</summary>
	public string? Background { get; set; }
	/// <summary>The audio file name, if any.</summary>
	public string? Audio { get; set; }
	/// <summary>The video file name, if any.</summary>
	public string? Video { get; set; }
	/// <summary>The preview start, in milliseconds, if set.</summary>
	public int? PreviewTime { get; set; }
	/// <summary>Whether the beatmap has a storyboard.</summary>
	public bool Storyboard { get; set; }
	/// <summary>When the beatmap was first taken in.</summary>
	public DateTimeOffset CreatedAt { get; set; }
	/// <summary>When the beatmap was last updated.</summary>
	public DateTimeOffset UpdatedAt { get; set; }
	/// <summary>Whether the beatmap is locked against replacement.</summary>
	public bool Locked { get; set; }
	/// <summary>Whether the beatmap is shown to players.</summary>
	public bool Visible { get; set; }
	/// <summary>The beatmapset the beatmap belongs to.</summary>
	public BeatmapsetRow Set { get; set; } = null!;
}

internal sealed class BeatmapRowConfiguration : IEntityTypeConfiguration<BeatmapRow>
{
	/// <inheritdoc />
	public void Configure(EntityTypeBuilder<BeatmapRow> builder)
	{
		builder.ToTable("beatmaps", table => table.HasCheckConstraint("beatmaps_id_check", "id >= 1"));
		builder.HasKey(row => row.Id);
		builder.Property(row => row.Id).ValueGeneratedNever();
		builder.Property(row => row.Objects).HasConversion(
			objects => objects.GetRawText(),
			json => JsonSerializer.Deserialize<JsonElement>(json, (JsonSerializerOptions?)null));
		builder.Property(row => row.Hash).HasMaxLength(128);
		builder.HasIndex(row => row.Hash).IsUnique();
		builder.HasIndex(row => row.BeatmapsetId);
		builder.HasIndex(row => row.Mode);
	}
}