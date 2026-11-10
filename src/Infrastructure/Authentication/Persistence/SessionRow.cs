using System.Net;
using Basil.Domain.Authentication;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Authentication.Persistence;

/// <summary>A stored row of the <c>sessions</c> table.</summary>
internal sealed class SessionRow
{
	/// <summary>The unique identifier of the session.</summary>
	public long Id { get; set; }
	/// <summary>The account the session belongs to.</summary>
	public int UserId { get; set; }
	/// <summary>The kind of client the session was opened for.</summary>
	public SessionType Type { get; set; }
	/// <summary>The address the session was opened from.</summary>
	public IPAddress Ip { get; set; } = IPAddress.None;
	/// <summary>The client information the session announced, if any.</summary>
	public string? ClientInfo { get; set; }
	/// <summary>When the session started.</summary>
	public DateTimeOffset StartedAt { get; set; }
	/// <summary>When the session was last active.</summary>
	public DateTimeOffset LastActiveAt { get; set; }
	/// <summary>When the session ended, or <see langword="null" /> while it lasts.</summary>
	public DateTimeOffset? EndedAt { get; set; }
	/// <summary>Why the session ended, or <see langword="null" /> while it lasts.</summary>
	public SessionEndReason? EndReason { get; set; }
	/// <summary>The hash of the token that proves the session.</summary>
	public byte[] TokenHash { get; set; } = [];
	/// <summary>When the session expires, or <see langword="null" /> when it does not.</summary>
	public DateTimeOffset? ExpiresAt { get; set; }
	/// <summary>The build date of the osu! client, if reported.</summary>
	public string? ClientDate { get; set; }
	/// <summary>The revision of the osu! client build, if reported.</summary>
	public int? ClientRevision { get; set; }
	/// <summary>The release stream of the osu! client build, if reported.</summary>
	public int? ClientStream { get; set; }
	/// <summary>The hash of the osu! install path the client reported.</summary>
	public string? OsuPathHash { get; set; }
	/// <summary>The network adapters the client reported.</summary>
	public string? NetworkAdapters { get; set; }
	/// <summary>The hash of the network adapters the client reported.</summary>
	public string? NetworkAdaptersHash { get; set; }
	/// <summary>The hash of the uninstall identifier the client reported.</summary>
	public string? UninstallHash { get; set; }
	/// <summary>The hash of the disk signature the client reported.</summary>
	public string? DiskSignatureHash { get; set; }
}

internal sealed class SessionRowConfiguration : IEntityTypeConfiguration<SessionRow>
{
	/// <inheritdoc />
	public void Configure(EntityTypeBuilder<SessionRow> builder)
	{
		builder.ToTable("sessions");
		builder.HasKey(row => row.Id);
		builder.Property(row => row.Id).ValueGeneratedNever();
		builder.Property(row => row.Ip).HasConversion<string>().HasMaxLength(45);
		builder.HasOne(ContextModel.UsersTable, null).WithMany().HasForeignKey("UserId");
		builder.Property(row => row.TokenHash).HasMaxLength(32);
		builder.HasIndex(row => row.TokenHash).IsUnique();
		builder.HasIndex(row => new { row.UserId, row.StartedAt }).IsDescending(false, true);
	}
}