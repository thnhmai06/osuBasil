using Microsoft.EntityFrameworkCore.Design;

namespace Basil.Infrastructure.Common.Persistence;

internal sealed class CommonDbContextFactory : IDesignTimeDbContextFactory<CommonDbContext>
{
	public CommonDbContext CreateDbContext(string[] args)
	{
		return DesignTimeDatabase.CreateContext<CommonDbContext>();
	}
}