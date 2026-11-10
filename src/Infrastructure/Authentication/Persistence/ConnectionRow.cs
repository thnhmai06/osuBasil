using System.Net;
using Basil.Domain.Authentication;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Authentication.Persistence;

/// <summary>A stored row of the <c>connections</c> table.</summary>
internal sealed class ConnectionRow
{
	public long Id { get; set; }
	public long SessionId { get; set; }
	public IPAddress Ip { get; set; } = IPAddress.None;
	public string? ClientInfo { get; set; }
	public DateTimeOffset OpenedAt { get; set; }
	public DateTimeOffset LastActiveAt { get; set; }
	public DateTimeOffset? ClosedAt { get; set; }
	public SessionEndReason? CloseReason { get; set; }
}

internal sealed class ConnectionRowConfiguration : IEntityTypeConfiguration<ConnectionRow>
{
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
