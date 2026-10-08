using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Basil.Infrastructure.Storage.Writing;

/// <summary>Stores the changes the repositories make, side by side in lanes, each lane in the order its changes were made.</summary>
/// <remarks>
///     A snapshot never waits; a try-operation waits for its result, which is only given once it is committed. Changes
///     of one root keep their order. On shutdown every queued change is stored before the server stops, unless the
///     shutdown times out, in which case the lost changes are logged.
/// </remarks>
internal sealed class DatabaseWriter(
	Database database,
	IOptions<DatabaseOptions> options,
	TimeProvider timeProvider,
	ILogger<DatabaseWriter> logger) : BackgroundService, IHostedLifecycleService
{
	private readonly CancellationTokenSource _abandon = new();
	private readonly WriteLane[] _lanes = CreateLanes(database, options, timeProvider, logger);

	/// <summary>Queues the state of an identity, replacing any queued state of the same identity.</summary>
	/// <returns>A task that completes once the state is committed; callers that only record a change need not wait.</returns>
	public Task EnqueueAsync(Root root, object identity, WriteCommand command) =>
		LaneOf(root).Enqueue(identity, command);

	/// <summary>Queues a write that no other write replaces, such as a line of history.</summary>
	/// <returns>A task that completes once the write is committed.</returns>
	public Task AppendAsync(Root root, WriteCommand command) => EnqueueAsync(root, new object(), command);

	/// <summary>Runs a write whose outcome the caller needs, such as an insert that assigns an id.</summary>
	/// <returns>The result, once the write is committed.</returns>
	public async Task<T> WriteAsync<T>(Root root,
		Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> command, CancellationToken cancellationToken = default)
	{
		T result = default!;
		await LaneOf(root).Enqueue(async (connection, transaction) => result = await command(connection, transaction))
			.WaitAsync(cancellationToken);
		return result;
	}

	public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public async Task StoppedAsync(CancellationToken cancellationToken)
	{
		await using var registration = cancellationToken.Register(_abandon.Cancel);
		if (ExecuteTask is { } executeTask)
			await executeTask;
		await Task.WhenAll(_lanes.Select(lane => lane.StorePendingAsync(_abandon.Token)));
	}

	protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
		Task.WhenAll(_lanes.Select(lane => RunLaneAsync(lane, stoppingToken)));

	public override void Dispose()
	{
		_abandon.Dispose();
		base.Dispose();
	}

	private WriteLane LaneOf(Root root) => _lanes[(uint)root.GetHashCode() % (uint)_lanes.Length];

	private static WriteLane[] CreateLanes(Database database, IOptions<DatabaseOptions> options,
		TimeProvider timeProvider, ILogger<DatabaseWriter> logger)
	{
		if (options.Value.WriteLanes < 1)
			throw new ArgumentOutOfRangeException(nameof(options), options.Value.WriteLanes,
				"At least one write lane is required.");

		return Enumerable.Range(0, options.Value.WriteLanes)
			.Select(_ => new WriteLane(database, timeProvider, logger))
			.ToArray();
	}

	private async Task RunLaneAsync(WriteLane lane, CancellationToken stoppingToken)
	{
		while (true)
		{
			try
			{
				await lane.WaitUntilDueAsync(stoppingToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			await lane.StoreDueAsync(_abandon.Token);
		}
	}
}