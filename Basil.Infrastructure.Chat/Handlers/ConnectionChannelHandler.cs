using Basil.Application.Chat.Registry;
using Basil.Application.Sessions.Objects.Events;
using Basil.Infrastructure.Shared.Events;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class ConnectionChannelHandler(
	GeneralChannelRegistry channels,
	ILogger<ConnectionChannelHandler> logger) : INotificationHandler<SessionOpened>,
	INotificationHandler<SessionClosed>
{
	public ValueTask Handle(SessionClosed @event, CancellationToken ct)
	{
		HandlerStep.Run(logger, "part its channels", "the closed connection of user", @event.Session.Record.Id,
			() => channels.PartAll(@event.Session), ct);
		return ValueTask.CompletedTask;
	}

	public ValueTask Handle(SessionOpened @event, CancellationToken ct)
	{
		channels.JoinAutoChannels(@event.Session);
		return ValueTask.CompletedTask;
	}
}