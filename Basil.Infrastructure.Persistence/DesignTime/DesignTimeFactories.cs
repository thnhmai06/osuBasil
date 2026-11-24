using Basil.Infrastructure.Sessions.Persistence;
using Basil.Infrastructure.Beatmaps.Persistence;
using Basil.Infrastructure.Chat.Persistence;
using Basil.Infrastructure.Content.Persistence;
using Basil.Infrastructure.Multiplayer.Persistence;
using Basil.Infrastructure.Scores.Persistence;
using Basil.Infrastructure.Shared.Persistence;
using Basil.Infrastructure.Users.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Basil.Infrastructure.Persistence.DesignTime;

// Tools such as `dotnet ef migrations add` build a module context through these, with the conventions of the server.

internal sealed class SharedDbContextFactory : IDesignTimeDbContextFactory<SharedDbContext>
{
	public SharedDbContext CreateDbContext(string[] args)
	{
		return DesignTime.Create<SharedDbContext>();
	}
}

internal sealed class UsersDbContextFactory : IDesignTimeDbContextFactory<UsersDbContext>
{
	public UsersDbContext CreateDbContext(string[] args)
	{
		return DesignTime.Create<UsersDbContext>();
	}
}

internal sealed class SessionsDbContextFactory : IDesignTimeDbContextFactory<SessionsDbContext>
{
	public SessionsDbContext CreateDbContext(string[] args)
	{
		return DesignTime.Create<SessionsDbContext>();
	}
}

internal sealed class BeatmapsDbContextFactory : IDesignTimeDbContextFactory<BeatmapsDbContext>
{
	public BeatmapsDbContext CreateDbContext(string[] args)
	{
		return DesignTime.Create<BeatmapsDbContext>();
	}
}

internal sealed class ContentDbContextFactory : IDesignTimeDbContextFactory<ContentDbContext>
{
	public ContentDbContext CreateDbContext(string[] args)
	{
		return DesignTime.Create<ContentDbContext>();
	}
}

internal sealed class ScoresDbContextFactory : IDesignTimeDbContextFactory<ScoresDbContext>
{
	public ScoresDbContext CreateDbContext(string[] args)
	{
		return DesignTime.Create<ScoresDbContext>();
	}
}

internal sealed class MultiplayerDbContextFactory : IDesignTimeDbContextFactory<MultiplayerDbContext>
{
	public MultiplayerDbContext CreateDbContext(string[] args)
	{
		return DesignTime.Create<MultiplayerDbContext>();
	}
}

internal sealed class ChatDbContextFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
	public ChatDbContext CreateDbContext(string[] args)
	{
		return DesignTime.Create<ChatDbContext>();
	}
}

internal static class DesignTime
{
	public static TContext Create<TContext>() where TContext : DbContext, IDatabaseContext<TContext>
	{
		var builder = new DbContextOptionsBuilder<TContext>();
		builder.UseNpgsql("Host=localhost;Database=basil",
				postgres => postgres.MigrationsHistoryTable(TContext.HistoryTable)
					.MigrationsAssembly(typeof(DesignTime).Assembly.GetName().Name))
			.UseSnakeCaseNamingConvention();
		return TContext.Create(builder.Options);
	}
}