using Basil.Application.Sessions.Objects.Events;
using Basil.Application.Sessions.Registry;
using Basil.Infrastructure.Sessions.Metrics;
using System.Diagnostics.Metrics;
using Basil.Application.Sessions.Persistence;
using Basil.Application.Users.Objects;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class SessionEndHandler(
	ISessionRepository sessions,
	SessionRegistry registry,
	ILogger<SessionEndHandler> logger)
	: INotificationHandler<SessionClosed>
{
	internal static readonly Counter<long> UpdateFailures = SessionsMeter.Instance
		.CreateCounter<long>("basil.sessions.update.failed", description: "Sessions whose closing was not stored.");

	public async ValueTask Handle(SessionClosed @event, CancellationToken ct)
	{
		var session = @event.Session.State;
		try
		{
			await sessions.EndAsync(session.Record.Id, session.Record.Value.Ended!.Value);
			registry.ConfirmEnded(session.Record.Id);
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !ct.IsCancellationRequested)
		{
			UpdateFailures.Add(1);
			logger.LogError(exception, "Could not store the closing of session {SessionId} of user {UserId}",
				session.Record.Id, session.Record.Value.User.Id);
		}
	}
}