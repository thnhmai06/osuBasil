using Basil.Infrastructure.Shared.Persistence;
using Basil.Infrastructure.Shared.Registry;
using Basil.Infrastructure.Shared.Storage;
using Microsoft.Extensions.Hosting;

namespace Basil.Infrastructure.Shared.Workers;

internal sealed class StorageStartup(DataPaths paths, DatabaseMigrator migrator, IEnumerable<IResident> residents)
	: IHostedService
{
	public async Task StartAsync(CancellationToken ct)
	{
		paths.CreateDirectories();
		await migrator.MigrateAsync(ct);
		foreach (var resident in residents)
			await resident.LoadAsync(ct);
	}

	public Task StopAsync(CancellationToken ct)
	{
		return Task.CompletedTask;
	}
}