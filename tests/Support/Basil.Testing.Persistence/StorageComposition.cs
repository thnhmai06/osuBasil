using Basil.Infrastructure.Shared.Persistence;
using Basil.Infrastructure.Shared.Registry;
using Basil.Infrastructure.Shared.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Testing.Persistence;

public sealed class StorageComposition : IAsyncDisposable
{
	private readonly ServiceProvider _provider;
	private readonly string _root;

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
		services.AddSingleton(Options.Create(new StorageOptions { Location = _root }));
		services.AddSingleton(Options.Create(new DatabaseOptions
			{ WriteConnectionString = connectionString, WriteProvider = TestServer.ProviderName }));
		_provider = services.BuildServiceProvider();
	}

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

	public T Get<T>() where T : notnull
	{
		return _provider.GetRequiredService<T>();
	}

	public IResident ResidentOf<T>(T service) where T : notnull
	{
		return _provider.GetServices<IResident>().Single(resident => ReferenceEquals(resident, service));
	}
}