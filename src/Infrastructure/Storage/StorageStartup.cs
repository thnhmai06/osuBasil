using Microsoft.Extensions.Hosting;

namespace Basil.Infrastructure.Storage;

/// <summary>Prepares the server's storage before anything else uses it.</summary>
internal sealed class StorageStartup(DataPaths paths, DatabaseMigrator migrator) : IHostedService
{
	/// <summary>Creates the data directories and brings the database up to date.</summary>
	/// <param name="cancellationToken">A token that cancels the startup work.</param>
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		paths.CreateDirectories();
		await migrator.MigrateAsync(cancellationToken);
	}

	/// <summary>Does nothing; the storage needs no shutdown work.</summary>
	/// <param name="cancellationToken">A token that cancels the shutdown work.</param>
	/// <returns>A completed task.</returns>
	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
