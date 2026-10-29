using Basil.Testing.Clients;
using Basil.Testing.Persistence;

namespace Basil.Host.Tests.EndToEnd;

/// <summary>One real server for the end-to-end scenarios that join the osu!, IRC and HTTP hosts.</summary>
public sealed class HostEndToEndFixture : IAsyncLifetime
{
	private readonly PersistenceFixture _database = new();

	/// <summary>Gets the server every client of the collection talks to.</summary>
	public BasilHost Host { get; private set; } = null!;

	/// <summary>Gets the database server, for a scenario that starts a server of its own and stops it.</summary>
	public PersistenceFixture Database => _database;

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

[CollectionDefinition("Host end to end")]
public sealed class HostEndToEndCollection : ICollectionFixture<HostEndToEndFixture>;
