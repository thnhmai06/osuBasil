using Basil.Application.Chat.Registry;
using Basil.Application.Sessions.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class PmConnectionClosedHandler(PmChannels channels) : INotificationHandler<SessionClosed>
{
	public ValueTask Handle(SessionClosed @event, CancellationToken ct)
	{
		var user = @event.Session.Siblings;
		if (channels.Find(user) is { } channel)
			channel.ScopeTo(@event.Session).Part();

		if (@event.WentOffline && channels.Remove(user) is { } closed)
			closed.Close();

		return ValueTask.CompletedTask;
	}
}