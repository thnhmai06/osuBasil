using Basil.Application.Bot.Announcements;
using Basil.Application.Bot.Replies;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Infrastructure.Bot.Services;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

internal sealed class ForgetRoomClosing(RoomAnnouncements announcements) : INotificationHandler<LobbyRoomClosed>
{
	public ValueTask Handle(LobbyRoomClosed @event, CancellationToken ct)
	{
		announcements.TakeClosing(@event.Room);
		return ValueTask.CompletedTask;
	}
}
