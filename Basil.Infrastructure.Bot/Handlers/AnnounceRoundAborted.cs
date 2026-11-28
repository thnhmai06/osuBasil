using Basil.Application.Shared.Replies;
using Basil.Application.Bot.Announcements;
using Basil.Application.Bot.Replies;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Infrastructure.Bot.Services;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

internal sealed class AnnounceRoundAborted(RoomAnnouncements announcements, IReplies<BotReply> replies)
	: INotificationHandler<RoomRoundAborted>
{
	public async ValueTask Handle(RoomRoundAborted @event, CancellationToken ct)
	{
		await announcements.SayInRoomAsync(@event.Room, replies.Text(BotReply.AnnounceRoundAborted), ct);
	}
}
