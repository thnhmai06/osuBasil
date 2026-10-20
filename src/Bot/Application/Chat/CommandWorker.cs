using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Bot.Application.Chat;

/// <summary>Runs the bot's chat commands one after another, away from the channel event stream.</summary>
/// <remarks>
///     A command may wait for a room, a lookup or the store; the stream that announced the message must not wait with
///     it. Commands keep the order in which they were typed. The queue is bounded, one user cannot fill it, and a
///     command that takes too long is cancelled, so no user can hold the others up.
/// </remarks>
internal sealed class CommandWorker(ILogger<CommandWorker> logger) : BackgroundService
{
	/// <summary>How many commands may wait in all.</summary>
	internal const int QueueCapacity = 256;

	/// <summary>How many commands of one user may wait at once.</summary>
	internal const int PendingPerUser = 4;

	/// <summary>How long one command may run before it is cancelled.</summary>
	internal static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

	private readonly Channel<(int UserId, Func<CancellationToken, Task> Run)> _commands =
		Channel.CreateBounded<(int UserId, Func<CancellationToken, Task> Run)>(
			new BoundedChannelOptions(QueueCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

	private readonly ConcurrentDictionary<int, int> _pending = new();

	/// <summary>Queues a command to run as soon as the commands before it are done.</summary>
	/// <param name="userId">The user who typed the command.</param>
	/// <param name="command">The command to run; its failure is logged and never stops the worker.</param>
	/// <returns>
	///     <see langword="false" /> when the command was dropped because the user already has too many waiting or the
	///     queue is full.
	/// </returns>
	internal bool Enqueue(int userId, Func<CancellationToken, Task> command)
	{
		if (_pending.AddOrUpdate(userId, 1, (_, count) => count + 1) <= PendingPerUser
		    && _commands.Writer.TryWrite((userId, command))) return true;

		Release(userId);
		logger.LogWarning("Dropped a bot command from user {UserId}: too many commands waiting", userId);
		return false;
	}

	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		await foreach (var (userId, run) in _commands.Reader.ReadAllAsync(stoppingToken))
			try
			{
				using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
				timeout.CancelAfter(CommandTimeout);
				await run(timeout.Token);
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
		_pending.AddOrUpdate(userId, 0, (_, count) => Math.Max(0, count - 1));
	}
}