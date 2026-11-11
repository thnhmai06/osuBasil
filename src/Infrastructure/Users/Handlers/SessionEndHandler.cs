using System.Diagnostics.Metrics;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Users.Objects.Events;
using Basil.Infrastructure.Users.Metrics;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Stores the state of a session when its connection closes.</summary>
internal sealed class SessionEndHandler(ISessionRepository sessions, ILogger<SessionEndHandler> logger)
	: INotificationHandler<UserConnectionClosed>
{
	/// <summary>Counts the sessions whose closing could not be stored.</summary>
	internal static readonly Counter<long> UpdateFailures = UsersMeter.Instance
		.CreateCounter<long>("basil.sessions.update.failed", description: "Sessions whose closing was not stored.");

	/// <inheritdoc />
	public async ValueTask Handle(UserConnectionClosed @event, CancellationToken ct)
	{
		var session = @event.Connection.Snapshot();
		try
		{
			await sessions.CreateOrUpdateAsync(session);
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !ct.IsCancellationRequested)
		{
			UpdateFailures.Add(1);
			logger.LogError(exception, "Could not store the closing of session {SessionId} of user {UserId}",
				session.Id, session.Value.User.Id);
		}
	}
}