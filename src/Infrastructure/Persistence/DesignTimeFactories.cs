using Basil.Infrastructure.Authentication.Persistence;
using Basil.Infrastructure.Beatmaps.Persistence;
using Basil.Infrastructure.Chat.Persistence;
using Basil.Infrastructure.Content.Persistence;
using Basil.Infrastructure.Multiplayer.Persistence;
using Basil.Infrastructure.Scores.Persistence;
using Basil.Infrastructure.Users.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace Basil.Infrastructure.Persistence;

// Tools such as `dotnet ef migrations add` build a module context through these, with the conventions of the server.

internal sealed class UsersDbContextFactory : IDesignTimeDbContextFactory<UsersDbContext>
{
	public UsersDbContext CreateDbContext(string[] args)
	{
		return PostgresDatabaseProvider.CreateDesignTime<UsersDbContext>();
	}
}

internal sealed class AuthenticationDbContextFactory : IDesignTimeDbContextFactory<AuthenticationDbContext>
{
	public AuthenticationDbContext CreateDbContext(string[] args)
	{
		return PostgresDatabaseProvider.CreateDesignTime<AuthenticationDbContext>();
	}
}

internal sealed class BeatmapsDbContextFactory : IDesignTimeDbContextFactory<BeatmapsDbContext>
{
	public BeatmapsDbContext CreateDbContext(string[] args)
	{
		return PostgresDatabaseProvider.CreateDesignTime<BeatmapsDbContext>();
	}
}

internal sealed class ContentDbContextFactory : IDesignTimeDbContextFactory<ContentDbContext>
{
	public ContentDbContext CreateDbContext(string[] args)
	{
		return PostgresDatabaseProvider.CreateDesignTime<ContentDbContext>();
	}
}

internal sealed class ScoresDbContextFactory : IDesignTimeDbContextFactory<ScoresDbContext>
{
	public ScoresDbContext CreateDbContext(string[] args)
	{
		return PostgresDatabaseProvider.CreateDesignTime<ScoresDbContext>();
	}
}

internal sealed class MultiplayerDbContextFactory : IDesignTimeDbContextFactory<MultiplayerDbContext>
{
	public MultiplayerDbContext CreateDbContext(string[] args)
	{
		return PostgresDatabaseProvider.CreateDesignTime<MultiplayerDbContext>();
	}
}

internal sealed class ChatDbContextFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
	public ChatDbContext CreateDbContext(string[] args)
	{
		return PostgresDatabaseProvider.CreateDesignTime<ChatDbContext>();
	}
}