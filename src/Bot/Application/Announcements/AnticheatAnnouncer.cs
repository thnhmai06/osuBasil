using Basil.Application.Objects.Anticheat;
using Basil.Application.Objects.Events;
using Basil.Application.Objects.Multiplayer.Rooms;
using Basil.Application.Objects.Sessions;
using Basil.Application.Objects.Servers;
using Basil.Domain.Bancho;
using Basil.Domain.Users;

namespace Basil.Bot.Application.Announcements;

/// <summary>Announces anticheat flags in room channels and to room staff.</summary>
internal sealed class AnticheatAnnouncer(Server server, IUserRegistry users) : IEventHandler<AnticheatEvent>
{
	/// <inheritdoc />
	public async ValueTask HandleAsync(AnticheatEvent @event, CancellationToken cancellationToken)
	{
		if (@event is not AnticheatPlayerFlagged { Room: { } room } flagged)
			return;

		var reason = FormatFlags(flagged.Signs);
		var playerName = flagged.Player.Record.Value.Name;
		var channelMessage = string.Format(AnnouncementReplies.AnticheatFlagRoom, playerName, reason);
		await server.SayAsync(room.Channel, channelMessage, ct: cancellationToken);

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

		async Task WhisperIfOnlineAsync(UserRecord user)
		{
			if (users.Online.GetValueOrDefault(user) is { } online)
				await server.WhisperAsync(online, text, ct: cancellationToken);
		}
	}

	private static string FormatFlags(BanchoFlags flags)
	{
		return string.Join(", ", Enum.GetValues<BanchoFlags>()
			.Where(value => value != BanchoFlags.Clean && value != BanchoFlagsGroups.CheatSigns && flags.HasFlag(value))
			.Select(value => value.ToString()));
	}
}