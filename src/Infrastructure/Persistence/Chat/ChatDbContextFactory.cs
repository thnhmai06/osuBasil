using Basil.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore.Design;

namespace Basil.Infrastructure.Persistence.Chat;

internal sealed class ChatDbContextFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
	public ChatDbContext CreateDbContext(string[] args)
	{
		return DesignTimeDatabase.CreateContext<ChatDbContext>();
	}
}