using Basil.Application.Chat.Registry;
using Basil.Application.Shared.Events;
using Basil.Application.Users.Objects.Events;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class ConnectionChannelHandler(
	GeneralChannelRegistry channels,
	ILogger<ConnectionChannelHandler> logger) : INotificationHandler<UserSessionOpened>,
	INotificationHandler<UserSessionClosed>
{
	public ValueTask Handle(UserSessionClosed @event, CancellationToken ct)
	{
		HandlerStep.Run(logger, "part its channels", "the closed connection of user", @event.Session.Record.Id,
			() => channels.PartAll(@event.Session), ct);
		return ValueTask.CompletedTask;
	}

	public ValueTask Handle(UserSessionOpened @event, CancellationToken ct)
	{
		channels.JoinAutoChannels(@event.Session);
		return ValueTask.CompletedTask;
	}
}