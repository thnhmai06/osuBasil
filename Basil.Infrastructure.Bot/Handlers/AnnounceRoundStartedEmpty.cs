using Basil.Application.Bot.Announcements;
using Basil.Application.Bot.Replies;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Infrastructure.Bot.Services;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

internal sealed class AnnounceRoundStartedEmpty(RoomAnnouncements announcements, IReplies replies)
	: INotificationHandler<RoomRoundStarted>
{
	public async ValueTask Handle(RoomRoundStarted @event, CancellationToken ct)
	{
		if (@event.Players.Count == 0)
			await announcements.SayInRoomAsync(@event.Room, replies.Text(AnnouncementReplies.RoundStartedNoPlayers),
				ct);
	}
}
