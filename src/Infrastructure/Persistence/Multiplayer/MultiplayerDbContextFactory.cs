using Basil.Common.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore.Design;

namespace Basil.Infrastructure.Persistence.Multiplayer;

internal sealed class MultiplayerDbContextFactory : IDesignTimeDbContextFactory<MultiplayerDbContext>
{
	public MultiplayerDbContext CreateDbContext(string[] args)
	{
		return DesignTimeDatabase.CreateContext<MultiplayerDbContext>();
	}
}