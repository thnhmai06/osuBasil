using Basil.Infrastructure.Common.Postgres;
using Microsoft.EntityFrameworkCore.Design;

namespace Basil.Infrastructure.Content.Persistence;

internal sealed class ContentDbContextFactory : IDesignTimeDbContextFactory<ContentDbContext>
{
	public ContentDbContext CreateDbContext(string[] args)
	{
		return DesignTimeDatabase.CreateContext<ContentDbContext>();
	}
}