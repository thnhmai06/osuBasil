using Basil.LoadTests.Infrastructure.Metrics;

namespace Basil.LoadTests.Hosting;

/// <summary>
///     Owns the lifecycle of the Basil instance a load run is aimed at. Scenarios never know how the
///     server got there — swapping <c>dotnet run</c> for a published binary, Docker, or an
///     already-running instance is a profile edit (<see cref="Configuration.ServerHostSettings.Kind" />),
///     never a scenario-code edit.
/// </summary>
public interface IServerHost : IAsyncDisposable
{
	/// <summary>What this host is able to observe about the running server.</summary>
	ServerHostCapabilities Capabilities { get; }

	/// <summary>The address scenarios send requests to. Only valid after <see cref="StartAsync" /> completes.</summary>
	ServerEndpoint Endpoint { get; }

	/// <summary>Starts the server (a no-op for <see cref="ExistingServerHost" />).</summary>
	Task StartAsync(CancellationToken cancellationToken = default);

	/// <summary>Stops the server (a no-op for <see cref="ExistingServerHost" />).</summary>
	Task StopAsync(CancellationToken cancellationToken = default);

	/// <summary>Completes once the server answers requests or throws on timeout.</summary>
	/// <returns>
	///     The time from <see cref="StartAsync" /> to the first successful response, or
	///     <see langword="null" /> when this host did not start the server itself.
	/// </returns>
	Task<TimeSpan?> WaitUntilHealthyAsync(CancellationToken cancellationToken = default);

	/// <summary>
	///     Samples the server's current resource usage by delegating to whichever <see cref="IResourceSampler" />s
	///     this host owns — the sampling logic itself lives in <c>Infrastructure/Metrics</c> and is never
	///     duplicated per host implementation. Fields this host cannot observe are left null, per
	///     <see cref="Capabilities" />.
	/// </summary>
	Task<ResourceSample> CollectMetricsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	///     Writes the host-specific artifacts for this run (counters CSV, log tail, ...) into
	///     <paramref name="reportFolder" />.
	/// </summary>
	Task ExportResultsAsync(string reportFolder, CancellationToken cancellationToken = default);

	/// <summary>
	///     Snapshots the server's database to <paramref name="snapshotPath" />, or restores from it if it
	///     already exists. Refused (throws <see cref="NotSupportedException" />) when
	///     <see cref="ServerHostCapabilities.CanSnapshotDatabase" /> is <see langword="false" /> — a host
	///     that does not own the server's data must never touch it.
	/// </summary>
	/// <param name="snapshotPath">Path to the snapshot file, keyed by the caller on whatever makes two runs comparable.</param>
	/// <param name="restoreIfPresent">
	///     When <see langword="true" /> and the snapshot exists, restore it; otherwise capture the
	///     server's current database into it.
	/// </param>
	/// <returns><see langword="true" /> if a snapshot was restored; <see langword="false" /> if one was captured instead.</returns>
	Task<bool> SyncDatabaseSnapshotAsync(string snapshotPath, bool restoreIfPresent);
}