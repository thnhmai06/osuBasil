using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Users.Objects.Events;
using Mediator;

namespace Basil.Logging;

internal sealed class LoginLog(ILogger<LoginLog> logger) : INotificationHandler<UserSessionOpened>
{
	public ValueTask Handle(UserSessionOpened notification, CancellationToken ct)
	{
		var connection = notification.Session;
		logger.LogInformation("{Type} login of {User} ({UserId})", connection.Type, connection.Record.Value.Name,
			connection.Record.Id);
		return ValueTask.CompletedTask;
	}
}

internal sealed class LogoutLog(ILogger<LogoutLog> logger) : INotificationHandler<UserSessionClosed>
{
	public ValueTask Handle(UserSessionClosed notification, CancellationToken ct)
	{
		var connection = notification.Session;
		logger.LogInformation("{Type} logout of {User} ({UserId}): {Reason}", connection.Type,
			connection.Record.Value.Name,
			connection.Record.Id, notification.Reason);
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoomOpenedLog(ILogger<RoomOpenedLog> logger) : INotificationHandler<LobbyRoomOpened>
{
	public ValueTask Handle(LobbyRoomOpened notification, CancellationToken ct)
	{
		logger.LogInformation("Room {RoomId} opened", notification.Room.Id);
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoomClosedLog(ILogger<RoomClosedLog> logger) : INotificationHandler<LobbyRoomClosed>
{
	public ValueTask Handle(LobbyRoomClosed notification, CancellationToken ct)
	{
		logger.LogInformation("Room {RoomId} closed", notification.Room.Id);
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoundStartedLog(ILogger<RoundStartedLog> logger) : INotificationHandler<RoomRoundStarted>
{
	public ValueTask Handle(RoomRoundStarted notification, CancellationToken ct)
	{
		logger.LogInformation("Round {Round} started in room {RoomId} with {PlayerCount} players",
			notification.Round.Index,
			notification.Room.Id, notification.Players.Count);
		return ValueTask.CompletedTask;
	}
}

internal sealed class RoundEndedLog(ILogger<RoundEndedLog> logger) : INotificationHandler<RoomRoundEnded>
{
	public ValueTask Handle(RoomRoundEnded notification, CancellationToken ct)
	{
		logger.LogInformation("Round {Round} ended in room {RoomId}", notification.Round.Index, notification.Room.Id);
		return ValueTask.CompletedTask;
	}
}

internal sealed class SilenceLog(ILogger<SilenceLog> logger) : INotificationHandler<UserSilenced>
{
	public ValueTask Handle(UserSilenced notification, CancellationToken ct)
	{
		logger.LogInformation("User {UserId} was silenced", notification.User.Id);
		return ValueTask.CompletedTask;
	}
}