using Basil.Application.Chat.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Protocol.Bancho.Models.Multiplayer;
using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Host.Bancho.Support;

/// <summary>Translates the server's objects into what osu! clients show.</summary>
internal static class BanchoView
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

	/// <summary>Gets the role bits a client shows for an account.</summary>
	/// <param name="effective">The account's effective permissions.</param>
	/// <returns>The roles that at least one effective permission belongs to; the player bit when there is none.</returns>
	public static int Privileges(UserPermissions effective)
	{
		var roles = UserPrivileges.None;
		foreach (var role in Enum.GetValues<UserPrivileges>().Where(role => role != UserPrivileges.None))
			if ((role.Permissions & effective) != UserPermissions.None)
				roles |= role;

		return (int)(roles == UserPrivileges.None ? UserPrivileges.Player : roles);
	}

	/// <summary>Writes the presence packet of a user.</summary>
	/// <param name="writer">The writer to add the packet to.</param>
	/// <param name="user">The user.</param>
	/// <param name="stats">The user's statistics in the mode they are in.</param>
	/// <param name="now">The current time, for the effective permissions.</param>
	public static void WritePresence(PacketWriter writer, User user, UserStats stats, DateTimeOffset now)
	{
		var client = user.Bancho;
		writer.UserPresence(user.Record.Id, user.Record.Value.Name, client?.UtcOffset ?? 0,
			(int)user.Record.Value.Country, Privileges(user.EffectivePermissions(now)),
			(int)(client?.Status.GameMode ?? GameMode.Standard), 0, 0, stats.Rank);
	}

	/// <summary>Writes the stats packet of a user.</summary>
	/// <param name="writer">The writer to add the packet to.</param>
	/// <param name="user">The user.</param>
	/// <param name="stats">The user's statistics in the mode they are in.</param>
	public static void WriteStats(PacketWriter writer, User user, UserStats stats)
	{
		var status = user.Bancho?.Status ?? PlayerStatus.Idle;
		writer.UserStats(user.Record.Id, (int)status.Activity, status.Text, status.BeatmapHash?.ToString() ?? string.Empty,
			(int)status.GameMods, (int)status.GameMode, status.BeatmapId ?? 0, stats.RankedScore,
			UserStats.Accuracy, stats.PlayCount, stats.TotalScore, stats.Rank, UserStats.Pp);
	}

	/// <summary>Translates a room into the match a client shows.</summary>
	/// <param name="room">The room.</param>
	/// <returns>The match packet model.</returns>
	public static RoomPacket Match(Room room)
	{
		var slots = room.Slots.Select(slot => new RoomSlotPacket(SlotStatus(slot), slot.Team is { } team ? (int)team : 0,
			slot.Mods is { } mods ? (int)mods : 0, slot.Player?.Record.Id)).ToList();
		var beatmap = room.Settings.Beatmap;
		return new RoomPacket(room.Id, room.Rounds.InProgress, (int)room.Settings.MatchSettings.Mods, room.Settings.Name,
			room.Settings.Password, beatmap?.Name ?? string.Empty, beatmap?.Id ?? 0, beatmap?.Hash.ToString() ?? string.Empty,
			slots, room.Authority.Host?.Record.Id ?? RoomPacket.NoHostId, (int)room.Settings.MatchSettings.Mode,
			(int)room.Settings.MatchSettings.WinCondition, (int)room.Settings.MatchSettings.TeamType,
			room.Settings.MatchSettings.Freemods, room.Settings.MatchSettings.Seed);
	}

	private static int SlotStatus(RoomSlot slot)
	{
		if (slot.Player is null) return slot.Locked ? 2 : 1;
		return slot.Status switch
		{
			RoomSlotStatus.NoMap => 16,
			RoomSlotStatus.NotReady => 4,
			RoomSlotStatus.Ready => 8,
			RoomSlotStatus.Playing => 32,
			RoomSlotStatus.Complete => 64,
			_ => 4
		};
	}
}
