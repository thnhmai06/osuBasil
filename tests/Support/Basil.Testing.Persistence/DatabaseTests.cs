using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Shared.Persistence;
using Basil.Infrastructure.Shared.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Basil.Testing.Persistence;

public abstract class DatabaseTests(PersistenceFixture container) : IAsyncLifetime
{
	private readonly string _database = "basil-" + Guid.NewGuid().ToString("N");
	private readonly string _root = Path.Combine(Path.GetTempPath(), "basil-test-db-" + Guid.NewGuid().ToString("N"));
	private UserRecord? _matchCreator;
	private ServiceProvider? _provider;

	protected string ConnectionString { get; private set; } = "";

	public async ValueTask InitializeAsync()
	{
		var connection = await container.CreateDatabaseAsync(_database);
		ConnectionString = connection;
		var services = new ServiceCollection();
		services.AddBasilStorage();
		services.AddLogging();
		services.AddSingleton(Options.Create(new StorageOptions { Location = _root }));
		services.AddSingleton(Options.Create(new DatabaseOptions
			{ WriteConnectionString = connection, WriteProvider = TestServer.ProviderName }));
		_provider = services.BuildServiceProvider();

		foreach (var hosted in _provider.GetServices<IHostedService>())
			await hosted.StartAsync(TestContext.Current.CancellationToken);
	}

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

	protected T Get<T>() where T : notnull
	{
		return _provider!.GetRequiredService<T>();
	}

	protected async Task StoreRowsAsync<TContext>(IEnumerable<object> rows)
		where TContext : DbContext, IDatabaseContext<TContext>
	{
		await using var context = Get<Database>().CreateConfirmedContext<TContext>();
		context.AddRange(rows);
		await context.SaveChangesAsync(TestContext.Current.CancellationToken);
	}

	protected async Task<MatchRecord> CreateStoredMatchAsync(string name, DateTimeOffset startedAt,
		DateTimeOffset? endedAt = null, bool isPrivate = false)
	{
		_matchCreator ??= await Get<IUserRepository>().CreateAsync(new UserData { Name = "match_creator" });
		return await Get<IMatchRepository>().CreateAsync(
			StoredMatches.Data(name, startedAt, _matchCreator, endedAt, isPrivate));
	}
}