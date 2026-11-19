using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Bot.Workers;

internal sealed class CommandWorker(ILogger<CommandWorker> logger, TimeProvider time) : BackgroundService
{
	internal const int QueueCapacity = 256;

	internal const int PendingPerUser = 4;

	internal static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

	private readonly Channel<(int UserId, Func<CancellationToken, Task> Run)> _commands =
		Channel.CreateBounded<(int UserId, Func<CancellationToken, Task> Run)>(
			new BoundedChannelOptions(QueueCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

	private readonly ConcurrentDictionary<int, int> _pending = new();

	internal bool Enqueue(int userId, Func<CancellationToken, Task> command)
	{
		if (_pending.AddOrUpdate(userId, 1, (_, count) => count + 1) <= PendingPerUser
		    && _commands.Writer.TryWrite((userId, command))) return true;

		Release(userId);
		logger.LogWarning("Dropped a bot command from user {UserId}: too many commands waiting", userId);
		return false;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		await foreach (var (userId, run) in _commands.Reader.ReadAllAsync(stoppingToken))
			try
			{
				using var timeout = new CancellationTokenSource(CommandTimeout, time);
				using var limited = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeout.Token);
				await run(limited.Token);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
			catch (OperationCanceledException)
			{
				logger.LogWarning("A bot command from user {UserId} took longer than {Timeout} and was cancelled",
					userId,
					CommandTimeout);
			}
			catch (Exception exception)
			{
				logger.LogError(exception, "A bot command failed");
			}
			finally
			{
				Release(userId);
			}
	}

	private void Release(int userId)
	{
		if (_pending.AddOrUpdate(userId, 0, (_, count) => Math.Max(0, count - 1)) == 0)
			_pending.TryRemove(new KeyValuePair<int, int>(userId, 0));
	}
}