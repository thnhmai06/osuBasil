using Basil.Application.Contracts.Multiplayer.Events;
using Basil.Application.Storage.Multiplayer;
using Basil.Application.Storage.Sessions;
using Basil.Domain.Mechanics;

namespace Basil.Application.Services.Multiplayer;

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
		if (room.Banned.Contains(player.User))
			throw new InvalidOperationException("The user is banned from this room.");

		if (room.Slots.Find(player) is { } existing) return existing;

		var slot = room.Slots.FirstOrDefault(s => s is { Locked: false, Player: null });
		if (slot is null) return null;

		Occupy(slot, player);

		if (room.TeamType.NeedSplitTeam())
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

		Clear(slot);
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

	/// <summary>Makes a slot empty.</summary>
	/// <param name="slot">The slot to clear.</param>
	internal static void Clear(RoomSlot slot)
	{
		slot.Player = null;
		slot.Status = null;
		slot.Team = null;
		slot.Mods = null;
		slot.IntroSkipped = null;
		slot.Loaded = null;
	}

	/// <summary>Assigns a slot's status without reporting a <see cref="RoomSlotStatusChanged" /> event.</summary>
	/// <param name="slot">The slot to change.</param>
	/// <param name="status">The status to assign, or <see langword="null" /> to clear it.</param>
	/// <remarks>
	///     Playing starts the slot's intro-skipped and loaded flags as false; any other status clears them. Assigning the
	///     current status does nothing.
	/// </remarks>
	internal static void SetStatus(RoomSlot slot, RoomSlotStatus? status)
	{
		if (slot.Status == status) return;
		slot.Status = status;
		slot.IntroSkipped = status is RoomSlotStatus.Playing ? false : null;
		slot.Loaded = status is RoomSlotStatus.Playing ? false : null;
	}

	/// <summary>Moves a player, with their status, team, mods and round progress, to another slot.</summary>
	/// <param name="from">The slot the player leaves.</param>
	/// <param name="to">The open slot the player moves to.</param>
	/// <remarks>Moving to the same slot does nothing.</remarks>
	/// <exception cref="ArgumentException">The target slot belongs to a different room.</exception>
	/// <exception cref="InvalidOperationException">The source slot has no player, or the target slot is not open.</exception>
	internal static void MoveTo(RoomSlot from, RoomSlot to)
	{
		if (!ReferenceEquals(from.Slots, to.Slots))
			throw new ArgumentException("The target slot belongs to a different room.", nameof(to));
		if (from.Index == to.Index) return;
		if (from.Player is null) throw new InvalidOperationException("The slot has no player.");
		if (to.Locked || to.Player is not null)
			throw new InvalidOperationException("The target slot is not open.");

		Restore(to, Capture(from));
		Clear(from);
	}

	/// <summary>Reads everything that belongs to the player seated in a slot.</summary>
	/// <param name="slot">The occupied slot.</param>
	/// <returns>The occupant and their per-player state.</returns>
	internal static RoomSlotState Capture(RoomSlot slot)
	{
		return new RoomSlotState(slot.Player!, slot.Status, slot.Team, slot.Mods, slot.IntroSkipped, slot.Loaded);
	}

	/// <summary>Seats a captured player in a slot with their per-player state, replacing whatever the slot held.</summary>
	/// <param name="slot">The slot to fill.</param>
	/// <param name="state">The state to restore.</param>
	internal static void Restore(RoomSlot slot, RoomSlotState state)
	{
		slot.Player = state.Player;
		slot.Status = state.Status;
		slot.Team = state.Team;
		slot.Mods = state.Mods;
		slot.IntroSkipped = state.IntroSkipped;
		slot.Loaded = state.Loaded;
	}

	/// <summary>Sets every playing or finished player back to not ready.</summary>
	/// <param name="room">The room whose players to reset.</param>
	internal static void ResetPlayers(Room room)
	{
		foreach (var slot in room.Slots.Where(s => s.Status is RoomSlotStatus.Playing or RoomSlotStatus.Complete))
			SetStatus(slot, RoomSlotStatus.NotReady);
	}

	private static void PassHostFrom(Room room, BanchoConnection leaving)
	{
		if (!ReferenceEquals(room.Host, leaving)) return;
		room.Host = room.Slots.FirstOrDefault(slot => slot.Player is not null && !ReferenceEquals(slot.Player, leaving))
			?.Player;
	}

	/// <summary>Everything that belongs to a seated player, as it is carried from one slot to another.</summary>
	/// <param name="Player">The seated connection.</param>
	/// <param name="Status">The player's status.</param>
	/// <param name="Team">The player's team.</param>
	/// <param name="Mods">The player's own mods.</param>
	/// <param name="IntroSkipped">Whether the player skipped the intro.</param>
	/// <param name="Loaded">Whether the player loaded the beatmap.</param>
	internal readonly record struct RoomSlotState(
		BanchoConnection Player,
		RoomSlotStatus? Status,
		GameTeam? Team,
		GameMods? Mods,
		bool? IntroSkipped,
		bool? Loaded);
}