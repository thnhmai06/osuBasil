using Basil.Infrastructure.Common.Postgres;
using Microsoft.EntityFrameworkCore.Design;

namespace Basil.Infrastructure.Chat.Persistence;

internal sealed class ChatDbContextFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
	public ChatDbContext CreateDbContext(string[] args)
	{
		return DesignTimeDatabase.CreateContext<ChatDbContext>();
	}
}