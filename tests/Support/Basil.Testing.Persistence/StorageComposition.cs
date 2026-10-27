using Basil.Infrastructure.Common;
using Basil.Infrastructure.Common.Postgres;
using Basil.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Testing.Persistence;

/// <summary>
///     A second composition of the server's storage, built against a database that already exists. Its services start
///     out empty: the resident data is loaded only when the test asks for it, so a test can see what a fresh server
///     would read from the database.
/// </summary>
public sealed class StorageComposition : IAsyncDisposable
{
	private readonly ServiceProvider _provider;
	private readonly string _root;

	/// <summary>Builds a composition of the storage against a database.</summary>
	/// <param name="connectionString">How to reach the database.</param>
	/// <param name="time">The clock the storage reads, or <see langword="null" /> for the system clock.</param>
	/// <param name="logs">Receives the storage's log entries, or <see langword="null" /> for none.</param>
	/// <param name="configure">Changes the registrations after the storage's own, or <see langword="null" /> for none.</param>
	public StorageComposition(string connectionString, TimeProvider? time = null, ILoggerProvider? logs = null,
		Action<IServiceCollection>? configure = null)
	{
		_root = Path.Combine(Path.GetTempPath(), "basil-test-storage-" + Guid.NewGuid().ToString("N"));
		var services = new ServiceCollection();
		if (time is not null) services.AddSingleton(time);
		services.AddBasilStorage();
		configure?.Invoke(services);
		services.AddLogging(builder =>
		{
			if (logs is not null) builder.AddProvider(logs);
		});
		services.AddSingleton(Options.Create(new StorageOptions { DataDirectory = _root }));
		services.AddSingleton(Options.Create(new DatabaseOptions { ConnectionString = connectionString }));
		_provider = services.BuildServiceProvider();
	}

	/// <summary>Stops the storage and deletes the data directory.</summary>
	public async ValueTask DisposeAsync()
	{
		var hosted = _provider.GetServices<IHostedService>().ToArray();
		foreach (var service in hosted.Reverse())
		{
			if (service is IHostedLifecycleService lifecycle) await lifecycle.StoppingAsync(CancellationToken.None);
			await service.StopAsync(CancellationToken.None);
		}

		await _provider.DisposeAsync();
		if (Directory.Exists(_root)) Directory.Delete(_root, true);
	}

	/// <summary>Gets a service of the composition.</summary>
	/// <typeparam name="T">The type of the service.</typeparam>
	public T Get<T>() where T : notnull
	{
		return _provider.GetRequiredService<T>();
	}

	/// <summary>Gets the resident that loads the given service, so a test can load it as a fresh server would.</summary>
	/// <typeparam name="T">The type of the resident service.</typeparam>
	/// <param name="service">The service the resident loads.</param>
	/// <returns>The resident that loads <paramref name="service" />.</returns>
	public IResident ResidentOf<T>(T service) where T : notnull
	{
		return _provider.GetServices<IResident>().Single(resident => ReferenceEquals(resident, service));
	}
}