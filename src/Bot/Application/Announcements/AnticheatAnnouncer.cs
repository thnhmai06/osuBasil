using Basil.Bot.Application.Basil;
using Basil.Domain.Client;
using Basil.Domain.Users;

namespace Basil.Bot.Application.Announcements;

/// <summary>Announces anticheat flags in room channels and to room staff.</summary>
internal sealed class AnticheatAnnouncer(IBasilChat chat, IBasilRooms rooms)
{
	/// <summary>Handles an anticheat event.</summary>
	public async Task HandleAsync(BotEvent botEvent, CancellationToken cancellationToken)
	{
		if (botEvent is PlayerFlagged { RoomId: not null } flagged)
		{
			await HandlePlayerFlaggedAsync(flagged, cancellationToken);
		}
	}

	private async Task HandlePlayerFlaggedAsync(PlayerFlagged e, CancellationToken cancellationToken)
	{
		var roomId = e.RoomId!.Value;
		var room = await rooms.GetAsync(roomId, cancellationToken);
		if (room is null)
			return;

		var reason = FormatFlags(e.Signs);
		var playerName = e.Player.Value.Name;

		// Post in room channel
		var channelMessage = string.Format(AnnouncementReplies.AnticheatFlagRoom, playerName, reason);
		await chat.PostAsync($"#mp_{roomId}", channelMessage, cancellationToken);

		// PM to creator and referees
		var pmMessage = string.Format(AnnouncementReplies.AnticheatFlagPm, roomId, room.Name, playerName, reason);

		var recipients = new List<User>();
		if (room.Creator is not null)
			recipients.Add(room.Creator);
		recipients.AddRange(room.Referees);

		foreach (var recipient in recipients)
		{
			await chat.SendAsync(recipient, pmMessage, cancellationToken);
		}
	}

	private static string FormatFlags(ClientFlags flags)
	{
		var flagNames = new List<string>();
		foreach (var value in Enum.GetValues<ClientFlags>())
		{
			if (value == ClientFlags.Clean || value == ClientFlags.CheatSigns)
				continue;

			if (flags.HasFlag(value))
			{
				flagNames.Add(value.ToString());
			}
		}
		return string.Join(", ", flagNames);
	}
}
