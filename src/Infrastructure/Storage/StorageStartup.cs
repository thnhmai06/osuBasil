using Microsoft.Extensions.Hosting;

namespace Basil.Infrastructure.Storage;

/// <summary>Prepares the server's storage before anything else uses it.</summary>
internal sealed class StorageStartup : IHostedService
{
	private readonly DataPaths paths;
	private readonly DatabaseMigrator migrator;

	/// <summary>Creates the startup work for the resolved paths and the migrator.</summary>
	/// <param name="paths">The paths the server stores files at.</param>
	/// <param name="migrator">The migrator that brings the database up to date.</param>
	public StorageStartup(DataPaths paths, DatabaseMigrator migrator)
	{
		this.paths = paths;
		this.migrator = migrator;
	}

	/// <summary>Creates the data directories and brings the database up to date.</summary>
	/// <param name="cancellationToken">A token that cancels the startup work.</param>
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		paths.CreateDirectories();
		await migrator.MigrateAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Does nothing; the storage needs no shutdown work.</summary>
	/// <param name="cancellationToken">A token that cancels the shutdown work.</param>
	/// <returns>A completed task.</returns>
	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
