using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Users.Persistence;

internal sealed class UserRow
{
	public int Id { get; set; }

	public string Name { get; set; } = "";

	public string NormalizedName { get; set; } = "";

	public string? ActiveName { get; set; }

	public int Country { get; set; }

	public int Privileges { get; set; }

	public DateTimeOffset? SilencedUntil { get; set; }

	public DateTimeOffset? DeletedAt { get; set; }
}

internal sealed class UserRowConfiguration : IEntityTypeConfiguration<UserRow>
{
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