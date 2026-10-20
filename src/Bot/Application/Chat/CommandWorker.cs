using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Bot.Application.Chat;

/// <summary>Runs the bot's chat commands one after another, away from the channel event stream.</summary>
/// <remarks>
///     A command may wait for a room, a lookup or the store; the stream that announced the message must not wait with
///     it. Commands keep the order in which they were typed.
/// </remarks>
internal sealed class CommandWorker(ILogger<CommandWorker> logger) : BackgroundService
{
	private readonly Channel<Func<CancellationToken, Task>> _commands =
		Channel.CreateUnbounded<Func<CancellationToken, Task>>(new UnboundedChannelOptions { SingleReader = true });

	/// <summary>Queues a command to run as soon as the commands before it are done.</summary>
	/// <param name="command">The command to run; its failure is logged and never stops the worker.</param>
	internal void Enqueue(Func<CancellationToken, Task> command)
	{
		_commands.Writer.TryWrite(command);
	}

	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		await foreach (var command in _commands.Reader.ReadAllAsync(stoppingToken))
			try
			{
				await command(stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception exception)
			{
				logger.LogError(exception, "A bot command failed");
			}
	}
}
