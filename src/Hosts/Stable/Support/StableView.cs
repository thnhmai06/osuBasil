using Basil.Application.Chat.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Protocol.Stable.Models.Multiplayer;
using Basil.Protocol.Stable.Wire.Packets;

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

	public static void WritePresence(PacketWriter writer, User user, UserStats stats, DateTimeOffset now)
	{
		var client = user.Stable;
		writer.UserPresence(user.Record.Id, user.Record.Value.Name, client?.UtcOffset ?? 0,
			(int)user.Record.Value.Country, Privileges(user.EffectivePrivileges(now)),
			(int)(client?.Status.GameMode ?? GameMode.Standard), 0, 0, stats.Rank);
	}

	public static void WriteStats(PacketWriter writer, User user, UserStats stats)
	{
		var status = user.Stable?.Status ?? PlayerStatus.Idle;
		writer.UserStats(user.Record.Id, (int)status.Activity, status.Text,
			status.BeatmapHash?.ToString() ?? string.Empty,
			(int)status.GameMods, (int)status.GameMode, status.BeatmapId ?? 0,
			stats.RankedScore,
			UserStats.Accuracy, stats.PlayCount, stats.TotalScore, stats.Rank, UserStats.Pp);
	}

	public static RoomPacket Match(Room room)
	{
		var slots = room.Slots.Select(slot => new RoomSlotPacket(SlotStatus(slot),
			(int)WireMap.ToWire(slot.Team),
			slot.Mods is { } mods ? (int)mods : 0, slot.Player?.Record.Id)).ToList();
		var beatmap = room.Settings.Beatmap;
		return new RoomPacket(room.Id, room.Rounds.InProgress, (int)room.Settings.MatchSettings.Mods,
			room.Settings.Name,
			room.Settings.Password, beatmap?.Name ?? string.Empty, beatmap?.Id ?? 0,
			beatmap?.Hash.ToString() ?? string.Empty,
			slots, room.Authority.Host?.Record.Id ?? RoomPacket.NoHostId,
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