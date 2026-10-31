using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Infrastructure.Common;
using Basil.Infrastructure.Common.Persistence;
using Basil.Host;
using Basil.Infrastructure.Common.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Basil.Testing.Persistence;

/// <summary>
///     Runs a test against the test run's PostgreSQL container: a fresh database is created for the test, the
///     storage services register with it and start up (directories, migrations, residents), and every
///     repository reads and writes that database through the same composition the server runs.
/// </summary>
public abstract class DatabaseTests(PersistenceFixture container) : IAsyncLifetime
{
	private readonly string _database = "basil-" + Guid.NewGuid().ToString("N");
	private readonly string _root = Path.Combine(Path.GetTempPath(), "basil-test-db-" + Guid.NewGuid().ToString("N"));
	private UserRecord? _matchCreator;
	private ServiceProvider? _provider;

	/// <summary>The connection string of the database created for the test.</summary>
	protected string ConnectionString { get; private set; } = "";

	/// <summary>Creates and starts the storage for a fresh database.</summary>
	public async ValueTask InitializeAsync()
	{
		var connection = await container.CreateDatabaseAsync(_database);
		ConnectionString = connection;
		var services = new ServiceCollection();
		services.AddBasilStorage();
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

	/// <summary>Creates a match in the store, created by an account the test shares.</summary>
	/// <param name="name">The name of the match.</param>
	/// <param name="startedAt">The moment the match started.</param>
	/// <param name="endedAt">The moment the match ended, or <see langword="null" /> while it runs.</param>
	/// <param name="isPrivate">Whether the match history is visible only to its creator and participants.</param>
	/// <returns>The stored match.</returns>
	protected async Task<MatchRecord> CreateStoredMatchAsync(string name, DateTimeOffset startedAt,
		DateTimeOffset? endedAt = null, bool isPrivate = false)
	{
		_matchCreator ??= await Get<IUserRepository>().CreateAsync(new UserData { Name = "match_creator" },
			TestContext.Current.CancellationToken);
		return await Get<IMatchRepository>().CreateAsync(
			StoredMatches.Data(name, startedAt, _matchCreator, endedAt, isPrivate),
			TestContext.Current.CancellationToken);
	}
}