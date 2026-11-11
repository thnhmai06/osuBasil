using Basil.Infrastructure.Authentication.Persistence;
using Basil.Infrastructure.Beatmaps.Persistence;
using Basil.Infrastructure.Chat.Persistence;
using Basil.Infrastructure.Common.Persistence;
using Basil.Infrastructure.Content.Persistence;
using Basil.Infrastructure.Multiplayer.Persistence;
using Basil.Infrastructure.Scores.Persistence;
using Basil.Infrastructure.Users.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace Basil.Infrastructure.Persistence;

// Tools such as `dotnet ef migrations add` build a module context through these, with the conventions of the server.

internal sealed class CommonDbContextFactory : IDesignTimeDbContextFactory<CommonDbContext>
{
	/// <inheritdoc />
	public CommonDbContext CreateDbContext(string[] args)
	{
		return PostgresDatabaseProvider.CreateDesignTime<CommonDbContext>();
	}
}

internal sealed class UsersDbContextFactory : IDesignTimeDbContextFactory<UsersDbContext>
{
	/// <inheritdoc />
	public UsersDbContext CreateDbContext(string[] args)
	{
		return PostgresDatabaseProvider.CreateDesignTime<UsersDbContext>();
	}
}

internal sealed class AuthenticationDbContextFactory : IDesignTimeDbContextFactory<AuthenticationDbContext>
{
	/// <inheritdoc />
	public AuthenticationDbContext CreateDbContext(string[] args)
	{
		return PostgresDatabaseProvider.CreateDesignTime<AuthenticationDbContext>();
	}
}

internal sealed class BeatmapsDbContextFactory : IDesignTimeDbContextFactory<BeatmapsDbContext>
{
	/// <inheritdoc />
	public BeatmapsDbContext CreateDbContext(string[] args)
	{
		return PostgresDatabaseProvider.CreateDesignTime<BeatmapsDbContext>();
	}
}

internal sealed class ContentDbContextFactory : IDesignTimeDbContextFactory<ContentDbContext>
{
	/// <inheritdoc />
	public ContentDbContext CreateDbContext(string[] args)
	{
		return PostgresDatabaseProvider.CreateDesignTime<ContentDbContext>();
	}
}

internal sealed class ScoresDbContextFactory : IDesignTimeDbContextFactory<ScoresDbContext>
{
	/// <inheritdoc />
	public ScoresDbContext CreateDbContext(string[] args)
	{
		return PostgresDatabaseProvider.CreateDesignTime<ScoresDbContext>();
	}
}

internal sealed class MultiplayerDbContextFactory : IDesignTimeDbContextFactory<MultiplayerDbContext>
{
	/// <inheritdoc />
	public MultiplayerDbContext CreateDbContext(string[] args)
	{
		return PostgresDatabaseProvider.CreateDesignTime<MultiplayerDbContext>();
	}
}

internal sealed class ChatDbContextFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
	/// <inheritdoc />
	public ChatDbContext CreateDbContext(string[] args)
	{
		return PostgresDatabaseProvider.CreateDesignTime<ChatDbContext>();
	}
}