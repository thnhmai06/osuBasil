using Basil.Application.Chat.Objects;
using Basil.Domain.Game;
using Basil.Domain.Users;
using Basil.Domain.Multiplayer.Room;
using Basil.Protocol.Game.Stable.Models.Multiplayer;
using Basil.Protocol.Game.Stable.Wire.Packets;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;
using UserSessions = Basil.Application.Sessions.Objects.UserSessions;

namespace Basil.Host.Stable.Support;

internal static class StableView
{
	public const string MultiplayerAlias = "#multiplayer";

	public const string SpectatorAlias = "#spectator";

	public static string ChannelName(Channel channel)
	{
		return channel switch
		{
			RoomChannel => MultiplayerAlias,
			SpectatorChannel => SpectatorAlias,
			_ => channel.Name
		};
	}

	public static UserPrivileges Roles(UserPrivileges effective)
	{
		return effective == UserPrivileges.None ? UserPrivileges.Player : effective;
	}

	public static int Privileges(UserPrivileges effective)
	{
		return (int)Roles(effective);
	}

	public static void WritePresence(PacketWriter writer, UserSessions user, UserStats stats, DateTimeOffset now)
	{
		var client = user.Stable;
		writer.UserPresence(user.Record.Id, user.Record.Value.Name, client?.UtcOffset ?? 0,
			(int)user.Record.Value.Country, Privileges(user.EffectivePrivileges(now)),
			(int)(client?.Status.Detail?.Mode ?? GameMode.Standard), 0, 0, stats.Rank);
	}

	public static void WriteStats(PacketWriter writer, UserSessions user, UserStats stats)
	{
		var status = user.Stable?.Status ?? new UserStatus();
		var detail = status.Detail;
		writer.UserStats(user.Record.Id, (int)status.Activity, detail?.BeatmapReference.FullName ?? string.Empty,
			detail?.BeatmapReference.Hash.ToString() ?? string.Empty,
			(int)(detail?.Mods ?? GameMods.NoMod), (int)(detail?.Mode ?? GameMode.Standard),
			detail?.BeatmapReference.Id ?? 0,
			stats.RankedScore,
			UserStats.Accuracy, stats.PlayCount, stats.TotalScore, stats.Rank, UserStats.Pp);
	}

	public static RoomPacket Match(Room room)
	{
		var slots = room.Slots.Select(slot => new RoomSlotPacket(SlotStatus(slot),
			(int)WireMap.ToWire(slot.Team),
			slot.Mods is { } mods ? (int)mods : 0, slot.Player?.Id)).ToList();
		var beatmap = room.Settings.Beatmap;
		return new RoomPacket(room.Id, room.Rounds.InProgress, (int)room.Settings.MatchSettings.Mods,
			room.Settings.Name,
			room.Settings.Password, beatmap?.FullName ?? string.Empty, beatmap?.Id ?? 0,
			beatmap?.Hash.ToString() ?? string.Empty,
			slots, room.State.Members.Host?.Record.Id ?? RoomPacket.NoHostId,
			(int)room.Settings.MatchSettings.Mode,
			(int)room.Settings.MatchSettings.WinCondition,
			(int)room.Settings.MatchSettings.TeamType,
			room.Settings.MatchSettings.Freemods, room.Settings.MatchSettings.Seed);
	}

	private static int SlotStatus(RoomSlot slot)
	{
		if (slot.Player is null) return (int)(slot.Locked ? WireSlotStatus.Locked : WireSlotStatus.Open);
		return (int)(slot.Status is { } status ? WireMap.ToWire(status) : WireSlotStatus.NotReady);
	}
}