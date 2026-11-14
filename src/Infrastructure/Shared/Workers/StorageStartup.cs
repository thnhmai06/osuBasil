using Basil.Infrastructure.Shared.Persistence;
using Basil.Infrastructure.Shared.Registry;
using Basil.Infrastructure.Shared.Storage;
using Microsoft.Extensions.Hosting;

namespace Basil.Infrastructure.Shared.Workers;

/// <summary>Prepares the server's storage before anything else uses it.</summary>
internal sealed class StorageStartup(DataPaths paths, DatabaseMigrator migrator, IEnumerable<IResident> residents)
	: IHostedService
{
	/// <summary>Creates the data directories, brings the database up to date and loads resident data.</summary>
	/// <param name="ct">A token that cancels the startup work.</param>
	public async Task StartAsync(CancellationToken ct)
	{
		paths.CreateDirectories();
		await migrator.MigrateAsync(ct);
		foreach (var resident in residents)
			await resident.LoadAsync(ct);
	}

	/// <summary>Does nothing; the storage needs no shutdown work.</summary>
	/// <param name="ct">A token that cancels the shutdown work.</param>
	/// <returns>A completed task.</returns>
	public Task StopAsync(CancellationToken ct)
	{
		return Task.CompletedTask;
	}
}