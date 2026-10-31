using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace Basil.Infrastructure.Multiplayer.Persistence;

internal sealed class MultiplayerDbContextFactory : IDesignTimeDbContextFactory<MultiplayerDbContext>
{
	public MultiplayerDbContext CreateDbContext(string[] args)
	{
		return DesignTimeDatabase.CreateContext<MultiplayerDbContext>();
	}
}