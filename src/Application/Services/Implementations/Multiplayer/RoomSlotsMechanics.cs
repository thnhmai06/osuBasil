using Basil.Application.Services.Contracts.Multiplayer.Rooms;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Domain.Mechanics;

namespace Basil.Application.Services.Implementations.Multiplayer;

/// <summary>The slot-level mechanics the lobby and room services share: seating, vacating, resizing and moving players.</summary>
internal static class RoomSlotsMechanics
{
	/// <summary>Seats a player in the lowest-index empty, unlocked slot.</summary>
	/// <param name="room">The room to seat the player in.</param>
	/// <param name="player">The connection to seat.</param>
	/// <returns>The assigned slot, or <see langword="null" /> when no slot is available.</returns>
	/// <remarks>Seating a player who already has a slot returns that slot. The caller reports the seating.</remarks>
	/// <exception cref="InvalidOperationException">The user is banned from the room.</exception>
	internal static RoomSlot? Seat(Room room, BanchoConnection player)
	{
		if (room.Members.Banned.Contains(player.User))
			throw new InvalidOperationException("The user is banned from this room.");

		if (room.Slots.Find(player) is { } existing) return existing;

		var slot = room.Slots.FirstOrDefault(s => s is { Locked: false, Player: null });
		if (slot is null) return null;

		Occupy(slot, player);

		if (room.Settings.MatchSettings.TeamType.IsTeamMode())
		{
			var redCount = room.Slots.Count(s => s.Team == GameTeam.Red);
			var blueCount = room.Slots.Count(s => s.Team == GameTeam.Blue);
			slot.Team = redCount <= blueCount ? GameTeam.Red : GameTeam.Blue;
		}

		return slot;
	}

	/// <summary>Clears the slot occupied by a player, passing host on when the player was host.</summary>
	/// <param name="room">The room the player is seated in.</param>
	/// <param name="player">The connection to remove.</param>
	/// <returns>The slot that was vacated, or <see langword="null" /> when the connection was not seated.</returns>
	/// <remarks>When the host leaves, host passes to the next seated player by slot order.</remarks>
	internal static RoomSlot? Vacate(Room room, BanchoConnection player)
	{
		var slot = room.Slots.Find(player);
		if (slot is null) return null;

		PassHostFrom(room, player);

		slot.Clear();
		return slot;
	}

	/// <summary>Resizes the room, leaving occupied slots untouched.</summary>
	/// <param name="slots">The slots of the room to resize.</param>
	/// <param name="size">The new number of available slots, from 1 to 16.</param>
	internal static void Resize(RoomSlots slots, int size)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(size, RoomSlots.MaxSlotCount);

		// Players may sit in any slot, so the size counts usable slots rather than naming a slot
		// range: keep (size - players) empty slots open and lock the remaining empty ones.
		var open = size - slots.Count(s => s.Player is not null);
		foreach (var slot in slots.Where(s => s.Player is null))
			slot.Locked = open-- <= 0;
	}

	/// <summary>Seats a player in a slot.</summary>
	/// <param name="slot">The slot to occupy.</param>
	/// <param name="player">The connection to seat.</param>
	/// <exception cref="InvalidOperationException">The slot is locked or already occupied.</exception>
	internal static void Occupy(RoomSlot slot, BanchoConnection player)
	{
		if (slot.Locked) throw new InvalidOperationException("The slot is locked.");
		if (slot.Player is not null) throw new InvalidOperationException("The slot is already occupied.");

		slot.Player = player;
		slot.Status = RoomSlotStatus.NotReady;
		slot.Mods = GameMods.NoMod;
	}

	/// <summary>Sets every playing or finished player back to not ready.</summary>
	/// <param name="room">The room whose players to reset.</param>
	internal static void ResetPlayers(Room room)
	{
		foreach (var slot in room.Slots.Where(s => s.Status is RoomSlotStatus.Playing or RoomSlotStatus.Complete))
			slot.SetStatus(RoomSlotStatus.NotReady);
	}

	private static void PassHostFrom(Room room, BanchoConnection leaving)
	{
		if (!ReferenceEquals(room.Authority.Host, leaving)) return;
		room.Authority.Host = room.Slots
			.FirstOrDefault(slot => slot.Player is not null && !ReferenceEquals(slot.Player, leaving))
			?.Player;
	}
}