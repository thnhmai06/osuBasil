using Basil.Infrastructure.Common.Postgres;
using Microsoft.EntityFrameworkCore.Design;

namespace Basil.Infrastructure.Auth.Persistence;

internal sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
	public AuthDbContext CreateDbContext(string[] args)
	{
		return DesignTimeDatabase.CreateContext<AuthDbContext>();
	}
}