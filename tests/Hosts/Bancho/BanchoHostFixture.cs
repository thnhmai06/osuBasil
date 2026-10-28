using Basil.Testing.Clients;
using Basil.Testing.Persistence;

namespace Basil.Host.Bancho.Tests;

/// <summary>One server and one PostgreSQL container for all the osu! host tests, which use different usernames.</summary>
public sealed class BanchoHostFixture : IAsyncLifetime
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

[CollectionDefinition("Bancho host")]
public sealed class BanchoHostCollection : ICollectionFixture<BanchoHostFixture>;
