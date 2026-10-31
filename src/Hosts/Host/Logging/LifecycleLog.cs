using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Users.Objects.Events;
using Mediator;

namespace Basil.Host.Logging;

/// <summary>Logs a connection that opened: a login.</summary>
internal sealed class LoginLog(ILogger<LoginLog> logger) : INotificationHandler<UserConnectionOpened>
{
	public ValueTask Handle(UserConnectionOpened notification, CancellationToken cancellationToken)
	{
		var connection = notification.Connection;
		logger.LogInformation("{Type} login of {User} ({UserId})", connection.Type, connection.Record.Value.Name,
			connection.Record.Id);
		return ValueTask.CompletedTask;
	}
}

/// <summary>Logs a connection that closed: a logout.</summary>
internal sealed class LogoutLog(ILogger<LogoutLog> logger) : INotificationHandler<UserConnectionClosed>
{
	public ValueTask Handle(UserConnectionClosed notification, CancellationToken cancellationToken)
	{
		var connection = notification.Connection;
		logger.LogInformation("{Type} logout of {User} ({UserId}): {Reason}", connection.Type,
			connection.Record.Value.Name,
			connection.Record.Id, notification.Reason);
		return ValueTask.CompletedTask;
	}
}

/// <summary>Logs a room that opened.</summary>
internal sealed class RoomOpenedLog(ILogger<RoomOpenedLog> logger) : INotificationHandler<LobbyRoomOpened>
{
	public ValueTask Handle(LobbyRoomOpened notification, CancellationToken cancellationToken)
	{
		logger.LogInformation("Room {RoomId} opened", notification.Room.Id);
		return ValueTask.CompletedTask;
	}
}

/// <summary>Logs a room that closed.</summary>
internal sealed class RoomClosedLog(ILogger<RoomClosedLog> logger) : INotificationHandler<LobbyRoomClosed>
{
	public ValueTask Handle(LobbyRoomClosed notification, CancellationToken cancellationToken)
	{
		logger.LogInformation("Room {RoomId} closed", notification.Room.Id);
		return ValueTask.CompletedTask;
	}
}

/// <summary>Logs a round that started.</summary>
internal sealed class RoundStartedLog(ILogger<RoundStartedLog> logger) : INotificationHandler<RoomRoundStarted>
{
	public ValueTask Handle(RoomRoundStarted notification, CancellationToken cancellationToken)
	{
		logger.LogInformation("Round {Round} started in room {RoomId} with {PlayerCount} players",
			notification.Round.Index,
			notification.Room.Id, notification.Players.Count);
		return ValueTask.CompletedTask;
	}
}

/// <summary>Logs a round that ended.</summary>
internal sealed class RoundEndedLog(ILogger<RoundEndedLog> logger) : INotificationHandler<RoomRoundEnded>
{
	public ValueTask Handle(RoomRoundEnded notification, CancellationToken cancellationToken)
	{
		logger.LogInformation("Round {Round} ended in room {RoomId}", notification.Round.Index, notification.Room.Id);
		return ValueTask.CompletedTask;
	}
}

/// <summary>Logs a restriction: a moderation action.</summary>
internal sealed class RestrictionLog(ILogger<RestrictionLog> logger) : INotificationHandler<UserRestricted>
{
	public ValueTask Handle(UserRestricted notification, CancellationToken cancellationToken)
	{
		logger.LogInformation("User {UserId} was restricted", notification.User.Id);
		return ValueTask.CompletedTask;
	}
}