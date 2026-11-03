using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace Basil.Infrastructure.Authentication.Persistence;

internal sealed class AuthenticationDbContextFactory : IDesignTimeDbContextFactory<AuthenticationDbContext>
{
	public AuthenticationDbContext CreateDbContext(string[] args)
	{
		return DesignTimeDatabase.CreateContext<AuthenticationDbContext>();
	}
}