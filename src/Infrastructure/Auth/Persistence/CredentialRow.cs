using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Auth.Persistence;

/// <summary>A stored row of the <c>credentials</c> table.</summary>
internal sealed class CredentialRow
{
	public int UserId { get; set; }
	public string PasswordHash { get; set; } = "";
}

internal sealed class CredentialRowConfiguration : IEntityTypeConfiguration<CredentialRow>
{
	public void Configure(EntityTypeBuilder<CredentialRow> builder)
	{
		builder.ToTable("credentials");
		builder.HasKey(row => row.UserId);
		builder.HasOne(ContextModel.UsersTable, null).WithOne().HasForeignKey(typeof(CredentialRow), "UserId");
	}
}