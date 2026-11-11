using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Authentication.Persistence;

/// <summary>A stored row of the <c>credentials</c> table.</summary>
internal sealed class CredentialRow
{
	/// <summary>The account the credential belongs to.</summary>
	public int UserId { get; set; }

	/// <summary>The hashed password of the account.</summary>
	public string PasswordHash { get; set; } = "";
}

internal sealed class CredentialRowConfiguration : IEntityTypeConfiguration<CredentialRow>
{
	/// <inheritdoc />
	public void Configure(EntityTypeBuilder<CredentialRow> builder)
	{
		builder.ToTable("credentials");
		builder.HasKey(row => row.UserId);
		builder.HasOne(ContextModel.UsersTable, null).WithOne().HasForeignKey(typeof(CredentialRow), "UserId");
	}
}