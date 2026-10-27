using Basil.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore.Design;

namespace Basil.Infrastructure.Persistence.Beatmaps;

internal sealed class BeatmapsDbContextFactory : IDesignTimeDbContextFactory<BeatmapsDbContext>
{
	public BeatmapsDbContext CreateDbContext(string[] args)
	{
		return DesignTimeDatabase.CreateContext<BeatmapsDbContext>();
	}
}