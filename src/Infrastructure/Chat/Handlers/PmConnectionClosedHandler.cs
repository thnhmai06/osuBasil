using Basil.Application.Chat.Registry;
using Basil.Application.Users.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class PmConnectionClosedHandler(PmChannels channels) : INotificationHandler<UserConnectionClosed>
{
	public ValueTask Handle(UserConnectionClosed @event, CancellationToken ct)
	{
		var user = @event.Connection.User;
		if (channels.Find(user) is { } channel)
			channel.ScopeTo(@event.Connection).Part();

		if (@event.WentOffline && channels.Remove(user) is { } closed)
			closed.Close();

		return ValueTask.CompletedTask;
	}
}