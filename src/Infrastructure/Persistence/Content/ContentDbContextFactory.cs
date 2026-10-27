using Basil.Common.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore.Design;

namespace Basil.Infrastructure.Persistence.Content;

internal sealed class ContentDbContextFactory : IDesignTimeDbContextFactory<ContentDbContext>
{
	public ContentDbContext CreateDbContext(string[] args)
	{
		return DesignTimeDatabase.CreateContext<ContentDbContext>();
	}
}