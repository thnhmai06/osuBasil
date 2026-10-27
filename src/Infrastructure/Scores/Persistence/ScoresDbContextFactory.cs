using Basil.Infrastructure.Common.Postgres;
using Microsoft.EntityFrameworkCore.Design;

namespace Basil.Infrastructure.Scores.Persistence;

internal sealed class ScoresDbContextFactory : IDesignTimeDbContextFactory<ScoresDbContext>
{
	public ScoresDbContext CreateDbContext(string[] args)
	{
		return DesignTimeDatabase.CreateContext<ScoresDbContext>();
	}
}