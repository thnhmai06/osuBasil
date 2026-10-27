using Basil.Infrastructure.Common.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Scores.Persistence;

/// <summary>A stored row of the <c>scores</c> table.</summary>
internal sealed class ScoreRow
{
	public int Id { get; set; }
	public int? UserId { get; set; }
	public string? BeatmapHash { get; set; }
	public int Mode { get; set; }
	public int Mods { get; set; }
	public int Num300 { get; set; }
	public int Num100 { get; set; }
	public int Num50 { get; set; }
	public int NumGeki { get; set; }
	public int NumKatu { get; set; }
	public int NumMiss { get; set; }
	public int TotalScore { get; set; }
	public int MaxCombo { get; set; }
	public int CurrentCombo { get; set; }
	public int Grade { get; set; }
	public bool IsPassed { get; set; }
	public bool IsFullCombo { get; set; }
	public DateTimeOffset Timestamp { get; set; }
	public string? Checksum { get; set; }
}

internal sealed class ScoreRowConfiguration : IEntityTypeConfiguration<ScoreRow>
{
	public void Configure(EntityTypeBuilder<ScoreRow> builder)
	{
		builder.ToTable("scores", table => table.HasCheckConstraint("scores_id_check", "id >= 1"));
		builder.HasKey(row => row.Id);
		builder.Property(row => row.Id).UseIdentityByDefaultColumn();
		builder.HasOne(ContextModel.UsersTable, (string?)null).WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.NoAction);
		builder.HasIndex(row => row.UserId);
		builder.HasIndex(row => row.BeatmapHash);
		builder.HasIndex(row => row.Checksum).IsUnique();
	}
}