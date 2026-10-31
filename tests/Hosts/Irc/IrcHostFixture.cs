using Basil.Testing.Clients;
using Basil.Testing.Persistence;

namespace Basil.Host.Irc.Tests;

/// <summary>One server and one PostgreSQL container for all the IRC host tests, which use different usernames.</summary>
public sealed class IrcHostFixture : IAsyncLifetime
{
	private readonly PersistenceFixture _database = new();

	public BasilHost Host { get; private set; } = null!;

	public async ValueTask InitializeAsync()
	{
		await _database.InitializeAsync();
		Host = await BasilHost.StartAsync(_database);
		Host.OpenChannel("#osu");
	}

	public async ValueTask DisposeAsync()
	{
		await Host.DisposeAsync();
		await _database.DisposeAsync();
	}
}

[CollectionDefinition("IRC host")]
public sealed class IrcHostCollection : ICollectionFixture<IrcHostFixture>;