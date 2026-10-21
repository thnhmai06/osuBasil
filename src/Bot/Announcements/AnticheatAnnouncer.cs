using Basil.Application.Runtime.Anticheat;
using Basil.Application.Runtime.Events;
using Basil.Application.Runtime.Multiplayer.Rooms;
using Basil.Application.Runtime.Servers;
using Basil.Application.Runtime.Users;
using Basil.Domain.Bancho;
using Basil.Domain.Users;

namespace Basil.Bot.Announcements;

/// <summary>Announces anticheat flags in room channels and to room staff.</summary>
internal sealed class AnticheatAnnouncer(Server server, UserRegistry users) : IEventHandler<AnticheatEvent>
{
	/// <inheritdoc />
	public async ValueTask HandleAsync(AnticheatEvent @event, CancellationToken cancellationToken)
	{
		if (@event is not AnticheatPlayerFlagged { Room: { } room } flagged)
			return;

		var reason = FormatFlags(flagged.Signs);
		var playerName = flagged.Player.Record.Value.Name;
		var channelMessage = string.Format(AnnouncementReplies.AnticheatFlagRoom, playerName, reason);
		await server.SayAsync(room.Channel, channelMessage, cancellationToken: cancellationToken);

		var pmMessage = string.Format(AnnouncementReplies.AnticheatFlagPm, room.Id, room.Settings.Name,
			playerName, reason);
		await WhisperStaffAsync(room, pmMessage, cancellationToken);
	}

	private async Task WhisperStaffAsync(Room room, string text, CancellationToken cancellationToken)
	{
		if (room.Authority.Creator is { } creator)
			await WhisperIfOnlineAsync(creator);

		foreach (var referee in room.Authority.Referees)
			await WhisperIfOnlineAsync(referee);
		return;

		async Task WhisperIfOnlineAsync(UserRecord user)
		{
			if (users.Online.GetValueOrDefault(user) is { } online)
				await server.WhisperAsync(online, text, cancellationToken: cancellationToken);
		}
	}

	private static string FormatFlags(BanchoFlags flags)
	{
		return string.Join(", ", Enum.GetValues<BanchoFlags>()
			.Where(value => value != BanchoFlags.Clean && value != BanchoFlagsGroups.CheatSigns && flags.HasFlag(value))
			.Select(value => value.ToString()));
	}
}