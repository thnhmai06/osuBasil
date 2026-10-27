using Basil.Common.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore.Design;

namespace Basil.Infrastructure.Persistence.Users;

internal sealed class UsersDbContextFactory : IDesignTimeDbContextFactory<UsersDbContext>
{
	public UsersDbContext CreateDbContext(string[] args)
	{
		return DesignTimeDatabase.CreateContext<UsersDbContext>();
	}
}