using Basil.Infrastructure.Storage.Common;
using Basil.Infrastructure.Storage.Common.Database;
using Basil.Infrastructure.Storage.Common.Options;
using Microsoft.Extensions.Hosting;

namespace Basil.Infrastructure.Storage;

/// <summary>Prepares the server's storage before anything else uses it.</summary>
internal sealed class StorageStartup(DataPaths paths, DatabaseMigrator migrator, IEnumerable<IResident> residents)
	: IHostedService
{
	/// <summary>Creates the data directories, brings the database up to date and loads resident data.</summary>
	/// <param name="cancellationToken">A token that cancels the startup work.</param>
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		paths.CreateDirectories();
		await migrator.MigrateAsync(cancellationToken);
		foreach (var resident in residents)
			await resident.LoadAsync(cancellationToken);
	}

	/// <summary>Does nothing; the storage needs no shutdown work.</summary>
	/// <param name="cancellationToken">A token that cancels the shutdown work.</param>
	/// <returns>A completed task.</returns>
	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}