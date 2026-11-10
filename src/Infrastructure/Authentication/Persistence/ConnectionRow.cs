using System.Net;
using Basil.Domain.Authentication;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Authentication.Persistence;

/// <summary>A stored row of the <c>connections</c> table.</summary>
internal sealed class ConnectionRow
{
	/// <summary>The unique identifier of the connection.</summary>
	public long Id { get; set; }
	/// <summary>The session the connection belongs to.</summary>
	public long SessionId { get; set; }
	/// <summary>The address the connection came from.</summary>
	public IPAddress Ip { get; set; } = IPAddress.None;
	/// <summary>The client information the connection announced, if any.</summary>
	public string? ClientInfo { get; set; }
	/// <summary>When the connection was opened.</summary>
	public DateTimeOffset OpenedAt { get; set; }
	/// <summary>When the connection was last active.</summary>
	public DateTimeOffset LastActiveAt { get; set; }
	/// <summary>When the connection was closed, or <see langword="null" /> while it is open.</summary>
	public DateTimeOffset? ClosedAt { get; set; }
	/// <summary>Why the connection was closed, or <see langword="null" /> while it is open.</summary>
	public SessionEndReason? CloseReason { get; set; }
}

internal sealed class ConnectionRowConfiguration : IEntityTypeConfiguration<ConnectionRow>
{
	/// <inheritdoc />
	public void Configure(EntityTypeBuilder<ConnectionRow> builder)
	{
		builder.ToTable("connections");
		builder.HasKey(row => row.Id);
		builder.Property(row => row.Id).ValueGeneratedNever();
		builder.Property(row => row.Ip).HasConversion<string>().HasMaxLength(45);
		builder.HasOne<SessionRow>().WithMany().HasForeignKey(row => row.SessionId).OnDelete(DeleteBehavior.Cascade);
		builder.HasIndex(row => new { row.SessionId, row.OpenedAt }).IsDescending(false, true);
		builder.HasIndex(row => row.ClosedAt);
	}
}
