using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Persistence.Users;

/// <summary>A stored row of the <c>users</c> table.</summary>
internal sealed class UserRow
{
	public int Id { get; set; }
	public string Name { get; set; } = "";

	/// <summary>The identifying form of <see cref="Name" />, which is unique among the accounts that are not deleted.</summary>
	public string NormalizedName { get; set; } = "";

	public int Country { get; set; }
	public long Permissions { get; set; }
	public DateTimeOffset? DeletedAt { get; set; }
}

internal sealed class UserRowConfiguration : IEntityTypeConfiguration<UserRow>
{
	public void Configure(EntityTypeBuilder<UserRow> builder)
	{
		builder.ToTable("users", table => table.HasCheckConstraint("users_id_check", "id >= 1"));
		builder.HasKey(row => row.Id);
		builder.Property(row => row.Id).UseIdentityByDefaultColumn();
		builder.HasIndex(row => row.NormalizedName).IsUnique().HasFilter("deleted_at is null");
		// BasilBot, the fixed account the server speaks as: no permission, no credential. Accounts created later start at 2.
		builder.HasData(new UserRow { Id = 1, Name = "BasilBot", NormalizedName = "basilbot", Country = 244 });
	}
}
