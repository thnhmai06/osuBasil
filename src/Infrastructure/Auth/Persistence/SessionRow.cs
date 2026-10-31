using System.Net;
using Basil.Domain.Auth;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Auth.Persistence;

/// <summary>A stored row of the <c>sessions</c> table.</summary>
internal sealed class SessionRow
{
	public Guid Id { get; set; }
	public int UserId { get; set; }
	public SessionType Type { get; set; }
	public IPAddress Ip { get; set; } = IPAddress.None;
	public string? ClientInfo { get; set; }
	public DateTimeOffset StartedAt { get; set; }
	public DateTimeOffset LastActiveAt { get; set; }
	public DateTimeOffset? EndedAt { get; set; }
	public SessionEndReason? EndReason { get; set; }
	public byte[]? TokenHash { get; set; }
	public DateTimeOffset? ExpiresAt { get; set; }
	public bool MustReauthenticate { get; set; }
	public string? ClientDate { get; set; }
	public int? ClientRevision { get; set; }
	public int? ClientStream { get; set; }
	public string? OsuPathHash { get; set; }
	public string? NetworkAdapters { get; set; }
	public string? NetworkAdaptersHash { get; set; }
	public string? UninstallHash { get; set; }
	public string? DiskSignatureHash { get; set; }
}

internal sealed class SessionRowConfiguration : IEntityTypeConfiguration<SessionRow>
{
	public void Configure(EntityTypeBuilder<SessionRow> builder)
	{
		builder.ToTable("sessions");
		builder.HasKey(row => row.Id);
		builder.Property(row => row.Id).ValueGeneratedNever();
		builder.Property(row => row.Ip).HasColumnType("inet");
		builder.HasOne(ContextModel.UsersTable, (string?)null).WithMany().HasForeignKey("UserId");
		builder.HasIndex(row => row.TokenHash);
		builder.HasIndex(row => new { row.UserId, row.StartedAt }).IsDescending(false, true);
	}
}
