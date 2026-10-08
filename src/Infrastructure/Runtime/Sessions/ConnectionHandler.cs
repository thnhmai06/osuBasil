using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Infrastructure.Runtime.Events;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Runtime.Sessions;

/// <summary>Cleans up after connections: joins auto-join channels when one opens, and releases its room, spectating, lobby watch and channels when one closes.</summary>
internal sealed class ConnectionHandler(
	IChannelService channels,
	IRoomService rooms,
	ILobbyService lobby,
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
				await RunAsync("release its room", connection.User.Id,
					() => rooms.Members.ReleaseAsync(connection, cancellationToken), cancellationToken);
				Run("stop spectating", connection.User.Id,
					() => channels.Spectators.StopSpectating(connection), cancellationToken);
				if (connection is BanchoConnection bancho)
					Run("leave the lobby", connection.User.Id, () => lobby.Unwatch(bancho), cancellationToken);
				Run("part its channels", connection.User.Id,
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
		catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			logger.LogError(exception, "Could not {Step} for the closed connection of user {UserId}.", step, userId);
		}
	}

	/// <summary>Runs one cleanup step of a closed connection, logging a failure so the remaining steps still run.</summary>
	private void Run(string step, int userId, Action action, CancellationToken cancellationToken)
	{
		try
		{
			action();
		}
		catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			logger.LogError(exception, "Could not {Step} for the closed connection of user {UserId}.", step, userId);
		}
	}
}
