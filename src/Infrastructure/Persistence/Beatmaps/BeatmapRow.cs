using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Persistence.Beatmaps;

/// <summary>
///     A stored row of the <c>beatmaps</c> table. <see cref="Objects" /> is the JSON of a <c>BeatmapObjects</c> value;
///     <see cref="Length" /> and <see cref="PreviewTime" /> are in milliseconds.
/// </summary>
internal sealed class BeatmapRow
{
	public int Id { get; set; }
	public int BeatmapsetId { get; set; }
	public string Hash { get; set; } = "";
	public string Version { get; set; } = "";
	public int Mode { get; set; }
	public double Star { get; set; }
	public int Length { get; set; }
	public double Bpm { get; set; }
	public double Cs { get; set; }
	public double Ar { get; set; }
	public double Od { get; set; }
	public double Hp { get; set; }
	public JsonElement Objects { get; set; }
	public string? Background { get; set; }
	public string? Audio { get; set; }
	public string? Video { get; set; }
	public int? PreviewTime { get; set; }
	public bool Storyboard { get; set; }
	public DateTimeOffset CreatedAt { get; set; }
	public DateTimeOffset UpdatedAt { get; set; }
	public bool Locked { get; set; }
	public bool Visible { get; set; }
	public BeatmapsetRow Set { get; set; } = null!;
}

internal sealed class BeatmapRowConfiguration : IEntityTypeConfiguration<BeatmapRow>
{
	public void Configure(EntityTypeBuilder<BeatmapRow> builder)
	{
		builder.ToTable("beatmaps", table => table.HasCheckConstraint("beatmaps_id_check", "id >= 1"));
		builder.HasKey(row => row.Id);
		builder.Property(row => row.Id).UseSequence("local_beatmap_ids");
		builder.Property(row => row.Objects).HasColumnType("jsonb");
		builder.HasIndex(row => row.Hash).IsUnique();
		builder.HasIndex(row => row.BeatmapsetId);
		builder.HasIndex(row => row.Mode);
	}
}