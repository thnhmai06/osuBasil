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
	public int Id { get; set; }
	public string Artist { get; set; } = "";
	public string Title { get; set; } = "";
	public string Creator { get; set; } = "";
	public DateTimeOffset CreatedAt { get; set; }
	public DateTimeOffset UpdatedAt { get; set; }
	public string ArchiveHash { get; set; } = "";
	public long ArchiveSize { get; set; }
	public DateTimeOffset ArchiveModifiedAt { get; set; }
	public bool Storyboard { get; set; }
	public bool Locked { get; set; }
	public bool Visible { get; set; }
	public BeatmapsetStatus Status { get; set; }
	public List<BeatmapRow> Beatmaps { get; set; } = [];
}

internal sealed class BeatmapsetRowConfiguration : IEntityTypeConfiguration<BeatmapsetRow>
{
	public void Configure(EntityTypeBuilder<BeatmapsetRow> builder)
	{
		builder.ToTable("beatmapsets", table => table.HasCheckConstraint("beatmapsets_id_check", "id >= 1"));
		builder.HasKey(row => row.Id);
		builder.Property(row => row.Id).UseSequence("local_beatmapset_ids");
		builder.HasMany(row => row.Beatmaps).WithOne(row => row.Set).HasForeignKey(row => row.BeatmapsetId)
			.OnDelete(DeleteBehavior.Cascade);
	}
}