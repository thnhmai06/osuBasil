using Basil.Application.Runtime.Anticheat;
using Basil.Application.Runtime.Multiplayer.Rooms;
using Basil.Application.Runtime.Servers;
using Basil.Application.Runtime.Users;
using Basil.Bot.Replies;
using Basil.Domain.Bancho;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Bot.Announcements;

/// <summary>Announces anticheat flags in room channels and to room staff.</summary>
internal sealed class AnticheatAnnouncer(Server server, UserRegistry users, IReplies replies)
	: INotificationHandler<AnticheatPlayerFlagged>
{
	/// <inheritdoc />
	public async ValueTask Handle(AnticheatPlayerFlagged flagged, CancellationToken cancellationToken)
	{
		if (flagged.Room is not { } room)
			return;

		var reason = string.Join(", ", flagged.Signs.SignNames());
		var playerName = flagged.Player.Record.Value.Name;
		var channelMessage = replies.Text(AnnouncementReplies.AnticheatFlagRoom,
			new { player = playerName, reason });
		await server.SayAsync(room.Channel, channelMessage, cancellationToken: cancellationToken);

		var pmMessage = replies.Text(AnnouncementReplies.AnticheatFlagPm,
			new { room = room.Id, name = room.Settings.Name, player = playerName, reason });
		await WhisperStaffAsync(room, pmMessage, cancellationToken);
	}

	private async Task WhisperStaffAsync(Room room, string text, CancellationToken cancellationToken)
	{
		await WhisperIfOnlineAsync(room.Authority.Creator);

		foreach (var referee in room.Authority.Referees)
			await WhisperIfOnlineAsync(referee);
		return;

		async Task WhisperIfOnlineAsync(UserRecord user)
		{
			if (users.Online.GetValueOrDefault(user) is { } online)
				await server.WhisperAsync(online, text, cancellationToken: cancellationToken);
		}
	}
}