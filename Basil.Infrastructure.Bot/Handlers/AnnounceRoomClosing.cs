using Basil.Application.Bot.Announcements;
using Basil.Application.Bot.Replies;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Infrastructure.Bot.Services;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

internal sealed class AnnounceRoomClosing(RoomAnnouncements announcements, TimeProvider timeProvider, IReplies replies)
	: INotificationHandler<LobbyRoomClosingAnnounced>
{
	public async ValueTask Handle(LobbyRoomClosingAnnounced @event, CancellationToken ct)
	{
		var minutesUntilClose = (int)Math.Ceiling((@event.ClosesAt - timeProvider.GetUtcNow()).TotalMinutes);
		var text = replies.Text(AnnouncementReplies.RoomClosingAnnounced, new { minutes = minutesUntilClose });

		announcements.MarkClosing(@event.Room);
		await announcements.SayInRoomAsync(@event.Room, text, ct);
		await announcements.WhisperStaffAsync(@event.Room, text, ct);
	}
}
