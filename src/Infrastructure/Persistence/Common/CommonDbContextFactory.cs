using Basil.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore.Design;

namespace Basil.Infrastructure.Persistence.Common;

internal sealed class CommonDbContextFactory : IDesignTimeDbContextFactory<CommonDbContext>
{
	public CommonDbContext CreateDbContext(string[] args)
	{
		return DesignTimeDatabase.CreateContext<CommonDbContext>();
	}
}