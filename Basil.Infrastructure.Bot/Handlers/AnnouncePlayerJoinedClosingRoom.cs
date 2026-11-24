using Basil.Application.Bot.Announcements;
using Basil.Application.Bot.Replies;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Infrastructure.Bot.Services;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

internal sealed class AnnouncePlayerJoinedClosingRoom(RoomAnnouncements announcements, IReplies replies)
	: INotificationHandler<RoomPlayerJoined>
{
	public async ValueTask Handle(RoomPlayerJoined @event, CancellationToken ct)
	{
		if (announcements.TakeClosing(@event.Room))
			await announcements.SayInRoomAsync(@event.Room,
				replies.Text(AnnouncementReplies.RoomPlayerJoinedClosing), ct);
	}
}
