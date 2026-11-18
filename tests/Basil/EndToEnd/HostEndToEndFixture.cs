using Basil.Testing.Clients;
using Basil.Testing.Persistence;

namespace Basil.Tests.EndToEnd;

public sealed class HostEndToEndFixture : IAsyncLifetime
{
	public BasilHost Host { get; private set; } = null!;

	public PersistenceFixture Database { get; } = new();

	public async ValueTask InitializeAsync()
	{
		await Database.InitializeAsync();
		Host = await BasilHost.StartAsync(Database);
		Host.OpenChannel("#osu");
	}

	public async ValueTask DisposeAsync()
	{
		await Host.DisposeAsync();
		await Database.DisposeAsync();
	}
}

[CollectionDefinition("Host end to end")]
public sealed class HostEndToEndCollection : ICollectionFixture<HostEndToEndFixture>;