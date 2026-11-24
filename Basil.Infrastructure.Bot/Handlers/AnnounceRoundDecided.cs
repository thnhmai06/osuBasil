using Basil.Application.Shared.Replies;
using Basil.Application.Bot.Announcements;
using Basil.Application.Bot.Replies;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Infrastructure.Bot.Services;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

internal sealed class AnnounceRoundDecided(RoomAnnouncements announcements, IReplies<BotReply> replies)
	: INotificationHandler<RoomRoundDecided>
{
	public async ValueTask Handle(RoomRoundDecided @event, CancellationToken ct)
	{
		if (!announcements.TakeAwaitingScores(@event.Round)) return;

		if (@event.Missing.Count == 0)
		{
			await announcements.SayInRoomAsync(@event.Room, replies.Text(BotReply.AnnounceScoresReceived), ct);
			return;
		}

		await announcements.SayInRoomAsync(@event.Room, replies.Text(BotReply.AnnounceScoresReceivedExcept), ct);
		foreach (var player in @event.Missing)
			await announcements.SayInRoomAsync(@event.Room,
				replies.Text(BotReply.AnnounceScoreMissing, new { player = player.Value.Name }), ct);
	}
}
