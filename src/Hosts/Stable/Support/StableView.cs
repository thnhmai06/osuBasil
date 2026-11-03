using Basil.Application.Chat.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Protocol.Stable.Models.Multiplayer;
using Basil.Protocol.Stable.Models.Wire;
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
	/// <param name="effective">The account's effective permissions.</param>
	/// <returns>The roles that at least one effective permission belongs to; the player role when there is none.</returns>
	public static UserPrivileges Roles(UserPermissions effective)
	{
		var roles = effective.Privileges;
		return roles == UserPrivileges.None ? UserPrivileges.Player : roles;
	}

	/// <summary>Gets the role bits a client shows for an account.</summary>
	/// <param name="effective">The account's effective permissions.</param>
	/// <returns>The client's privilege bits for the account's roles.</returns>
	public static int Privileges(UserPermissions effective)
	{
		return (int)WireMap.ToWire(Roles(effective));
	}

	/// <summary>Writes the presence packet of a user.</summary>
	/// <param name="writer">The writer to add the packet to.</param>
	/// <param name="user">The user.</param>
	/// <param name="stats">The user's statistics in the mode they are in.</param>
	/// <param name="now">The current time, for the effective permissions.</param>
	public static void WritePresence(PacketWriter writer, User user, UserStats stats, DateTimeOffset now)
	{
		var client = user.Stable;
		writer.UserPresence(user.Record.Id, user.Record.Value.Name, client?.UtcOffset ?? 0,
			(int)user.Record.Value.Country, Privileges(user.EffectivePermissions(now)),
			(int)WireMap.ToWire(client?.Status.GameMode ?? GameMode.Standard), 0, 0, stats.Rank);
	}

	/// <summary>Writes the stats packet of a user.</summary>
	/// <param name="writer">The writer to add the packet to.</param>
	/// <param name="user">The user.</param>
	/// <param name="stats">The user's statistics in the mode they are in.</param>
	public static void WriteStats(PacketWriter writer, User user, UserStats stats)
	{
		var status = user.Stable?.Status ?? PlayerStatus.Idle;
		writer.UserStats(user.Record.Id, (int)WireMap.ToWire(status.Activity), status.Text,
			status.BeatmapHash?.ToString() ?? string.Empty,
			(int)WireMap.ToWire(status.GameMods), (int)WireMap.ToWire(status.GameMode), status.BeatmapId ?? 0,
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
			slot.Mods is { } mods ? (int)WireMap.ToWire(mods) : 0, slot.Player?.Record.Id)).ToList();
		var beatmap = room.Settings.Beatmap;
		return new RoomPacket(room.Id, room.Rounds.InProgress, (int)WireMap.ToWire(room.Settings.MatchSettings.Mods),
			room.Settings.Name,
			room.Settings.Password, beatmap?.Name ?? string.Empty, beatmap?.Id ?? 0,
			beatmap?.Hash.ToString() ?? string.Empty,
			slots, room.Authority.Host?.Record.Id ?? RoomPacket.NoHostId,
			(int)WireMap.ToWire(room.Settings.MatchSettings.Mode),
			(int)WireMap.ToWire(room.Settings.MatchSettings.WinCondition),
			(int)WireMap.ToWire(room.Settings.MatchSettings.TeamType),
			room.Settings.MatchSettings.Freemods, room.Settings.MatchSettings.Seed);
	}

	private static int SlotStatus(RoomSlot slot)
	{
		if (slot.Player is null) return (int)(slot.Locked ? WireSlotStatus.Locked : WireSlotStatus.Open);
		return (int)(slot.Status is { } status ? WireMap.ToWire(status) : WireSlotStatus.NotReady);
	}
}