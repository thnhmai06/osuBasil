using Basil.Common.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore.Design;

namespace Basil.Common.Infrastructure;

internal sealed class CommonDbContextFactory : IDesignTimeDbContextFactory<CommonDbContext>
{
	public CommonDbContext CreateDbContext(string[] args)
	{
		return DesignTimeDatabase.CreateContext<CommonDbContext>();
	}
}