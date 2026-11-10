using Basil.Application.Authentication.Persistence;
using Basil.Application.Users.Objects.Events;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Stores the closing of a connection in the trail of its session.</summary>
internal sealed class ConnectionClosedHandler(
	IConnectionRepository connections,
	TimeProvider time,
	ILogger<ConnectionClosedHandler> logger)
	: INotificationHandler<UserConnectionClosed>
{
	/// <inheritdoc />
	public async ValueTask Handle(UserConnectionClosed @event, CancellationToken ct)
	{
		var connection = @event.Connection;
		if (connection.IsServerOwned) return;

		try
		{
			await connections.CloseAsync(connection.Session.Id, connection.LastActiveAt, time.GetUtcNow(),
				@event.Reason);
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !ct.IsCancellationRequested)
		{
			SessionEndHandler.UpdateFailures.Add(1);
			logger.LogError(exception, "Could not store the closing of a connection of session {SessionId}",
				connection.Session.Id);
		}
	}
}
