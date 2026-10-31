using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace Basil.Infrastructure.Beatmaps.Persistence;

internal sealed class BeatmapsDbContextFactory : IDesignTimeDbContextFactory<BeatmapsDbContext>
{
	public BeatmapsDbContext CreateDbContext(string[] args)
	{
		return DesignTimeDatabase.CreateContext<BeatmapsDbContext>();
	}
}