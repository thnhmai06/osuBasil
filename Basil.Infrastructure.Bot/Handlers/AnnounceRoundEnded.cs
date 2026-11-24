using Basil.Application.Bot.Announcements;
using Basil.Application.Bot.Replies;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Infrastructure.Bot.Services;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

internal sealed class AnnounceRoundEnded(RoomAnnouncements announcements, IReplies replies)
	: INotificationHandler<RoomRoundEnded>
{
	public async ValueTask Handle(RoomRoundEnded @event, CancellationToken ct)
	{
		var seconds = (int)Math.Round(@event.ScoreWait.TotalSeconds);
		announcements.AwaitScores(@event.Round);
		await announcements.SayInRoomAsync(@event.Room,
			replies.Text(AnnouncementReplies.RoundEnded, new { id = @event.Round.Index, seconds }), ct);
	}
}
