using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Users.Persistence;

/// <summary>A stored row of the <c>users</c> table.</summary>
internal sealed class UserRow
{
	/// <summary>The unique identifier of the account.</summary>
	public int Id { get; set; }

	/// <summary>The account name.</summary>
	public string Name { get; set; } = "";

	/// <summary>The identifying form of <see cref="Name" />, of the account.</summary>
	public string NormalizedName { get; set; } = "";

	/// <summary><see cref="NormalizedName" /> while the account is not deleted, and nothing once it is; it is the unique name.</summary>
	public string? ActiveName { get; set; }

	/// <summary>The account country.</summary>
	public int Country { get; set; }

	/// <summary>The privileges of the account, as a bit set.</summary>
	public int Privileges { get; set; }

	/// <summary>When the silence ends, or <see langword="null" /> when the account is not silenced.</summary>
	public DateTimeOffset? SilencedUntil { get; set; }

	/// <summary>When the account was deleted, or <see langword="null" /> while it exists.</summary>
	public DateTimeOffset? DeletedAt { get; set; }
}

internal sealed class UserRowConfiguration : IEntityTypeConfiguration<UserRow>
{
	/// <inheritdoc />
	public void Configure(EntityTypeBuilder<UserRow> builder)
	{
		builder.ToTable("users", table => table.HasCheckConstraint("users_id_check", "id >= 1"));
		builder.HasKey(row => row.Id);
		// Id 1 is BasilBot, the fixed account the server speaks as; it is stored at startup under that id, so the
		// accounts the database numbers start at 2.
		builder.Property(row => row.Id).ValueGeneratedNever();
		builder.Property(row => row.NormalizedName).HasMaxLength(255);
		builder.Property(row => row.ActiveName).HasMaxLength(255);
		builder.HasIndex(row => row.NormalizedName);
		builder.HasIndex(row => row.ActiveName).IsUnique();
	}
}