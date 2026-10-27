using Basil.Common.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore.Design;

namespace Basil.Infrastructure.Persistence.Auth;

internal sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
	public AuthDbContext CreateDbContext(string[] args)
	{
		return DesignTimeDatabase.CreateContext<AuthDbContext>();
	}
}