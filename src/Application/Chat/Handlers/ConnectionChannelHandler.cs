using Basil.Application.Chat.Registry;
using Basil.Application.Common.Events;
using Basil.Application.Users.Objects.Events;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Chat.Handlers;

/// <summary>
///     Puts a connection into the channels that join themselves when it opens, and out of its channels when it
///     closes.
/// </summary>
internal sealed class ConnectionChannelHandler(
	GeneralChannelRegistry channels,
	ILogger<ConnectionChannelHandler> logger) : INotificationHandler<UserConnectionOpened>,
	INotificationHandler<UserConnectionClosed>
{
	/// <inheritdoc />
	public ValueTask Handle(UserConnectionClosed @event, CancellationToken cancellationToken)
	{
		HandlerStep.Run(logger, "part its channels", "the closed connection of user", @event.Connection.Record.Id,
			() => channels.PartAll(@event.Connection), cancellationToken);
		return ValueTask.CompletedTask;
	}

	/// <inheritdoc />
	public ValueTask Handle(UserConnectionOpened @event, CancellationToken cancellationToken)
	{
		channels.JoinAutoChannels(@event.Connection);
		return ValueTask.CompletedTask;
	}
}