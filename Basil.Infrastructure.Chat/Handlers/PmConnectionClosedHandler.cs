using Basil.Application.Chat.Registry;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class PmConnectionClosedHandler(PmChannels channels) : INotificationHandler<UserSessionClosed>
{
	public ValueTask Handle(UserSessionClosed @event, CancellationToken ct)
	{
		var user = @event.Session.User;
		if (channels.Find(user) is { } channel)
			channel.ScopeTo(@event.Session).Part();

		if (@event.WentOffline && channels.Remove(user) is { } closed)
			closed.Close();

		return ValueTask.CompletedTask;
	}
}