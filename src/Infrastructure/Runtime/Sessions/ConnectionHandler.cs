using Basil.Application.Objects.Chat;
using Basil.Application.Objects.Events;
using Basil.Application.Objects.Multiplayer;
using Basil.Application.Objects.Sessions;
using Basil.Application.Objects.Users;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Runtime.Sessions;

/// <summary>
///     Cleans up after connections: joins auto-join channels when one opens, and releases its room, spectating, lobby
///     watch and channels when one closes.
/// </summary>
internal sealed class ConnectionHandler(
	IChannelService channels,
	IRoomService rooms,
	Lobby lobby,
	UserRegistry registry,
	ILogger<ConnectionHandler> logger) : IEventHandler<UserEvent>
{
	/// <inheritdoc />
	public async ValueTask HandleAsync(UserEvent @event, CancellationToken cancellationToken)
	{
		switch (@event)
		{
			case UserConnectionOpened opened:
				channels.JoinAutoChannels(opened.Connection);
				break;
			case UserConnectionClosed closed:
				var connection = closed.Connection;
				await RunAsync("release its room", connection.Record.Id,
					() => rooms.Members.ReleaseAsync(connection, cancellationToken), cancellationToken);
				if (registry.FindSpectating(connection) is { } host)
					Run("stop spectating", connection.Record.Id,
						() => host.ScopeTo(connection).StopSpectating(), cancellationToken);
				if (connection is BanchoConnection bancho)
					Run("leave the lobby", connection.Record.Id, () => lobby.Unwatch(bancho), cancellationToken);
				Run("part its channels", connection.Record.Id,
					() => channels.PartAll(connection), cancellationToken);
				break;
		}
	}

	/// <summary>Runs one cleanup step of a closed connection, logging a failure so the remaining steps still run.</summary>
	private async Task RunAsync(string step, int userId, Func<Task> action, CancellationToken cancellationToken)
	{
		try
		{
			await action();
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !cancellationToken.IsCancellationRequested)
		{
			logger.LogError(exception, "Could not {Step} for the closed connection of user {UserId}", step, userId);
		}
	}

	/// <summary>Runs one cleanup step of a closed connection, logging a failure so the remaining steps still run.</summary>
	private void Run(string step, int userId, Action action, CancellationToken cancellationToken)
	{
		try
		{
			action();
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !cancellationToken.IsCancellationRequested)
		{
			logger.LogError(exception, "Could not {Step} for the closed connection of user {UserId}", step, userId);
		}
	}
}