using Basil.Infrastructure.Persistence.Common;
using Basil.Infrastructure.Persistence.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Basil.Infrastructure.Persistence.Tests.Support;

/// <summary>
///     Runs a test against the test run's PostgreSQL container: a fresh database is created for the test, the
///     storage services register with it and start up (directories, migrations, residents), and every
///     repository reads and writes that database through the same composition the server runs.
/// </summary>
public abstract class DatabaseTests(PersistenceFixture container) : IAsyncLifetime
{
	private string _database = "basil-" + Guid.NewGuid().ToString("N");
	private string _root = Path.Combine(Path.GetTempPath(), "basil-test-db-" + Guid.NewGuid().ToString("N"));
	private string _connection = "";
	private ServiceProvider? _provider;

	/// <summary>The connection string of the database created for the test.</summary>
	protected string ConnectionString => _connection;

	/// <summary>Creates and starts the storage for a fresh database.</summary>
	public async ValueTask InitializeAsync()
	{
		var connection = await container.CreateDatabaseAsync(_database);
		_connection = connection;
		var services = new ServiceCollection();
		services.AddInfrastructurePersistence();
		services.AddLogging();
		services.AddSingleton(Options.Create(new StorageOptions { DataDirectory = _root }));
		services.AddSingleton(Options.Create(new DatabaseOptions { ConnectionString = connection }));
		_provider = services.BuildServiceProvider();

		foreach (var hosted in _provider.GetServices<IHostedService>())
			await hosted.StartAsync(TestContext.Current.CancellationToken);
	}

	/// <summary>Stops the storage, drops the database and deletes the data directory.</summary>
	public async ValueTask DisposeAsync()
	{
		if (_provider is null) return;

		var hosted = _provider.GetServices<IHostedService>().ToArray();
		foreach (var service in hosted.Reverse())
			await service.StopAsync(TestContext.Current.CancellationToken);
		await _provider.DisposeAsync();

		await container.DropDatabaseAsync(_database);
		Directory.Delete(_root, true);
	}

	/// <summary>Gets a storage service of the freshly started composition.</summary>
	/// <typeparam name="T">The type of the service.</typeparam>
	protected T Get<T>() where T : notnull
	{
		return _provider!.GetRequiredService<T>();
	}
}
