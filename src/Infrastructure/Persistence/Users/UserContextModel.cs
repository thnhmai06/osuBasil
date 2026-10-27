using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Users;

internal static class UserContextModel
{
	/// <summary>Maps the users table without including it in this context's migrations.</summary>
	internal static void MapUserReference(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<UserRow>(row =>
		{
			row.ToTable("users", table => table.ExcludeFromMigrations());
			row.HasKey(user => user.Id);
		});
	}
}