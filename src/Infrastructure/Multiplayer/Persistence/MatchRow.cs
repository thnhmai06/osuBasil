using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Multiplayer.Persistence;

/// <summary>A stored row of the <c>matches</c> table.</summary>
internal sealed class MatchRow
{
	/// <summary>The unique identifier of the match.</summary>
	public int Id { get; set; }
	/// <summary>The room name.</summary>
	public string Name { get; set; } = "";
	/// <summary>The user who created the room.</summary>
	public int CreatorId { get; set; }
	/// <summary>When the match started.</summary>
	public DateTimeOffset StartedAt { get; set; }
	/// <summary>When the match ended, or <see langword="null" /> while it lasts.</summary>
	public DateTimeOffset? EndedAt { get; set; }
	/// <summary>Whether the room was private.</summary>
	public bool IsPrivate { get; set; }
}

internal sealed class MatchRowConfiguration : IEntityTypeConfiguration<MatchRow>
{
	/// <inheritdoc />
	public void Configure(EntityTypeBuilder<MatchRow> builder)
	{
		builder.ToTable("matches", table => table.HasCheckConstraint("matches_id_check", "id >= 1"));
		builder.HasKey(row => row.Id);
		builder.Property(row => row.Id).ValueGeneratedNever();
		builder.HasOne(ContextModel.UsersTable, null).WithMany().HasForeignKey("CreatorId")
			.IsRequired().OnDelete(DeleteBehavior.NoAction);
		builder.HasIndex(row => row.EndedAt);
	}
}