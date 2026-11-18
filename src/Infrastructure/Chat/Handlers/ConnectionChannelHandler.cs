using Basil.Application.Chat.Registry;
using Basil.Application.Shared.Events;
using Basil.Application.Users.Objects.Events;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class ConnectionChannelHandler(
	GeneralChannelRegistry channels,
	ILogger<ConnectionChannelHandler> logger) : INotificationHandler<UserConnectionOpened>,
	INotificationHandler<UserConnectionClosed>
{
	public ValueTask Handle(UserConnectionClosed @event, CancellationToken ct)
	{
		HandlerStep.Run(logger, "part its channels", "the closed connection of user", @event.Connection.Record.Id,
			() => channels.PartAll(@event.Connection), ct);
		return ValueTask.CompletedTask;
	}

	public ValueTask Handle(UserConnectionOpened @event, CancellationToken ct)
	{
		channels.JoinAutoChannels(@event.Connection);
		return ValueTask.CompletedTask;
	}
}