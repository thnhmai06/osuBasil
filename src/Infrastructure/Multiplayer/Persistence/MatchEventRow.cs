using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Multiplayer.Persistence;

/// <summary>A stored row of the <c>match_events</c> table.</summary>
internal sealed class MatchEventRow
{
	/// <summary>The unique identifier of the event.</summary>
	public long Id { get; set; }

	/// <summary>The match the event happened in.</summary>
	public int MatchId { get; set; }

	/// <summary>The kind of event.</summary>
	public int Type { get; set; }

	/// <summary>When the event happened.</summary>
	public DateTimeOffset Timestamp { get; set; }

	/// <summary>The user who caused the event, if any.</summary>
	public int? ActorId { get; set; }

	/// <summary>The user the event was aimed at, if any.</summary>
	public int? TargetId { get; set; }

	/// <summary>Further detail about the event, if any.</summary>
	public string? Detail { get; set; }
}

internal sealed class MatchEventRowConfiguration : IEntityTypeConfiguration<MatchEventRow>
{
	/// <inheritdoc />
	public void Configure(EntityTypeBuilder<MatchEventRow> builder)
	{
		builder.ToTable("match_events");
		builder.HasKey(row => row.Id);
		builder.Property(row => row.Id).ValueGeneratedNever();
		builder.HasOne<MatchRow>().WithMany().HasForeignKey(row => row.MatchId).OnDelete(DeleteBehavior.NoAction);
		builder.HasOne(ContextModel.UsersTable, null).WithMany().HasForeignKey("ActorId")
			.OnDelete(DeleteBehavior.NoAction);
		builder.HasOne(ContextModel.UsersTable, null).WithMany().HasForeignKey("TargetId")
			.OnDelete(DeleteBehavior.NoAction);
		builder.HasIndex(row => row.MatchId);
	}
}