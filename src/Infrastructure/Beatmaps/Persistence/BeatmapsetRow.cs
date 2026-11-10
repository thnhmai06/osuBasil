using Basil.Domain.Beatmaps;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Beatmaps.Persistence;

/// <summary>
///     A stored row of the <c>beatmapsets</c> table. The archive columns tell, without reading the archive, whether
///     it may have changed.
/// </summary>
internal sealed class BeatmapsetRow
{
	/// <summary>The unique identifier of the beatmapset.</summary>
	public int Id { get; set; }
	/// <summary>The song artist.</summary>
	public string Artist { get; set; } = "";
	/// <summary>The song title.</summary>
	public string Title { get; set; } = "";
	/// <summary>The mapper who made the set.</summary>
	public string Creator { get; set; } = "";
	/// <summary>When the set was first taken in.</summary>
	public DateTimeOffset CreatedAt { get; set; }
	/// <summary>When the set was last updated.</summary>
	public DateTimeOffset UpdatedAt { get; set; }
	/// <summary>The hash of the stored archive.</summary>
	public string ArchiveHash { get; set; } = "";
	/// <summary>The size of the stored archive, in bytes.</summary>
	public long ArchiveSize { get; set; }
	/// <summary>When the stored archive was last modified.</summary>
	public DateTimeOffset ArchiveModifiedAt { get; set; }
	/// <summary>Whether the set has a storyboard.</summary>
	public bool Storyboard { get; set; }
	/// <summary>Whether the set is locked against replacement.</summary>
	public bool Locked { get; set; }
	/// <summary>Whether the set is shown to players.</summary>
	public bool Visible { get; set; }
	/// <summary>The ranking status of the set.</summary>
	public BeatmapsetStatus Status { get; set; }
	/// <summary>The beatmaps of the set.</summary>
	public List<BeatmapRow> Beatmaps { get; set; } = [];
}

internal sealed class BeatmapsetRowConfiguration : IEntityTypeConfiguration<BeatmapsetRow>
{
	/// <inheritdoc />
	public void Configure(EntityTypeBuilder<BeatmapsetRow> builder)
	{
		builder.ToTable("beatmapsets", table => table.HasCheckConstraint("beatmapsets_id_check", "id >= 1"));
		builder.HasKey(row => row.Id);
		builder.Property(row => row.Id).ValueGeneratedNever();
		builder.Property(row => row.Status).HasConversion<int>();
		builder.HasMany(row => row.Beatmaps).WithOne(row => row.Set).HasForeignKey(row => row.BeatmapsetId)
			.OnDelete(DeleteBehavior.Cascade);
	}
}