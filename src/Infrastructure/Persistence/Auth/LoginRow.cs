using System.Net;
using Basil.Infrastructure.Persistence.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Persistence.Auth;

/// <summary>A stored row of the <c>logins</c> table.</summary>
internal sealed class LoginRow
{
	public long Id { get; set; }
	public int UserId { get; set; }
	public IPAddress Ip { get; set; } = IPAddress.None;
	public DateTimeOffset Timestamp { get; set; }
	public string? ClientDate { get; set; }
	public int? ClientRevision { get; set; }
	public int? ClientStream { get; set; }
	public string? OsuPathHash { get; set; }
	public string? NetworkAdapters { get; set; }
	public string? NetworkAdaptersHash { get; set; }
	public string? UninstallHash { get; set; }
	public string? DiskSignatureHash { get; set; }
}

internal sealed class LoginRowConfiguration : IEntityTypeConfiguration<LoginRow>
{
	public void Configure(EntityTypeBuilder<LoginRow> builder)
	{
		builder.ToTable("logins");
		builder.HasKey(row => row.Id);
		builder.Property(row => row.Id).UseIdentityAlwaysColumn();
		builder.Property(row => row.Ip).HasColumnType("inet");
		builder.HasOne<UserRow>().WithMany().HasForeignKey(row => row.UserId);
		builder.HasIndex(row => new { row.UserId, row.Timestamp }).IsDescending(false, true);
	}
}