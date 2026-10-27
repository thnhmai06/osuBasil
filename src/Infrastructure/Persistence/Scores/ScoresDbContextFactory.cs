using Basil.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore.Design;

namespace Basil.Infrastructure.Persistence.Scores;

internal sealed class ScoresDbContextFactory : IDesignTimeDbContextFactory<ScoresDbContext>
{
	public ScoresDbContext CreateDbContext(string[] args)
	{
		return DesignTimeDatabase.CreateContext<ScoresDbContext>();
	}
}