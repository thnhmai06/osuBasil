using Basil.Infrastructure.Common.Postgres;
using Microsoft.EntityFrameworkCore.Design;

namespace Basil.Infrastructure.Common;

internal sealed class CommonDbContextFactory : IDesignTimeDbContextFactory<CommonDbContext>
{
	public CommonDbContext CreateDbContext(string[] args)
	{
		return DesignTimeDatabase.CreateContext<CommonDbContext>();
	}
}