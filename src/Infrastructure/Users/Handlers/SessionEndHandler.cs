using System.Diagnostics.Metrics;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Users.Objects.Events;
using Basil.Infrastructure.Users.Metrics;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class SessionEndHandler(ISessionRepository sessions, ILogger<SessionEndHandler> logger)
	: INotificationHandler<UserSessionClosed>
{
	internal static readonly Counter<long> UpdateFailures = UsersMeter.Instance
		.CreateCounter<long>("basil.sessions.update.failed", description: "Sessions whose closing was not stored.");

	public async ValueTask Handle(UserSessionClosed @event, CancellationToken ct)
	{
		var session = @event.Session.State;
		try
		{
			await sessions.EndAsync(session.Id, session.Ended!.Value);
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !ct.IsCancellationRequested)
		{
			UpdateFailures.Add(1);
			logger.LogError(exception, "Could not store the closing of session {SessionId} of user {UserId}",
				session.Id, session.User.Id);
		}
	}
}