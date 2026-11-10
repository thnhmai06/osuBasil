using Basil.Application.Chat.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Protocol.Stable.Models.Multiplayer;
using Basil.Protocol.Stable.Wire.Packets;

namespace Basil.Host.Stable.Support;

/// <summary>Translates the server's objects into what osu! clients show.</summary>
internal static class StableView
{
	public const string MultiplayerAlias = "#multiplayer";
	public const string SpectatorAlias = "#spectator";

	/// <summary>Gets the name a client uses for a channel.</summary>
	/// <param name="channel">The channel.</param>
	/// <returns>The alias for a room or spectator channel, otherwise the channel's own name.</returns>
	public static string ChannelName(Channel channel)
	{
		return channel switch
		{
			RoomChannel => MultiplayerAlias,
			SpectatorChannel => SpectatorAlias,
			_ => channel.Name
		};
	}

	/// <summary>Gets the roles an account shows to clients.</summary>
	/// <param name="effective">The account's effective privileges.</param>
	/// <returns>The effective privileges; the player role when there is none.</returns>
	public static UserPrivileges Roles(UserPrivileges effective)
	{
		return effective == UserPrivileges.None ? UserPrivileges.Player : effective;
	}

	/// <summary>Gets the role bits a client shows for an account.</summary>
	/// <param name="effective">The account's effective privileges.</param>
	/// <returns>The client's privilege bits for the account's roles.</returns>
	public static int Privileges(UserPrivileges effective)
	{
		return (int)Roles(effective);
	}

	/// <summary>Writes the presence packet of a user.</summary>
	/// <param name="writer">The writer to add the packet to.</param>
	/// <param name="user">The user.</param>
	/// <param name="stats">The user's statistics in the mode they are in.</param>
	/// <param name="now">The current time, for the effective privileges.</param>
	public static void WritePresence(PacketWriter writer, User user, UserStats stats, DateTimeOffset now)
	{
		var client = user.Stable;
		writer.UserPresence(user.Record.Id, user.Record.Value.Name, client?.UtcOffset ?? 0,
			(int)user.Record.Value.Country, Privileges(user.EffectivePrivileges(now)),
			(int)(client?.Status.GameMode ?? GameMode.Standard), 0, 0, stats.Rank);
	}

	/// <summary>Writes the stats packet of a user.</summary>
	/// <param name="writer">The writer to add the packet to.</param>
	/// <param name="user">The user.</param>
	/// <param name="stats">The user's statistics in the mode they are in.</param>
	public static void WriteStats(PacketWriter writer, User user, UserStats stats)
	{
		var status = user.Stable?.Status ?? PlayerStatus.Idle;
		writer.UserStats(user.Record.Id, (int)status.Activity, status.Text,
			status.BeatmapHash?.ToString() ?? string.Empty,
			(int)status.GameMods, (int)status.GameMode, status.BeatmapId ?? 0,
			stats.RankedScore,
			UserStats.Accuracy, stats.PlayCount, stats.TotalScore, stats.Rank, UserStats.Pp);
	}

	/// <summary>Translates a room into the match a client shows.</summary>
	/// <param name="room">The room.</param>
	/// <returns>The match packet model.</returns>
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