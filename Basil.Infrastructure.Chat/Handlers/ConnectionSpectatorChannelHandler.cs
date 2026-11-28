using Basil.Application.Chat.Registry;
using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class ConnectionSpectatorChannelHandler(SpectatorChannels channels)
	: INotificationHandler<SessionClosed>
{
	public ValueTask Handle(SessionClosed @event, CancellationToken ct)
	{
		if (@event.Session is StableSession host && channels.Remove(host) is { } channel)
			channel.Close();

		return ValueTask.CompletedTask;
	}
}