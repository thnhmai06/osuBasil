using System.Collections;
using System.Collections.Immutable;
using Basil.Domain.Game;
using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer.Room;

/// <summary>The numbered slots of a room and the rules for seating players in them.</summary>
public sealed class RoomSlots : IReadOnlyList<RoomSlot>
{
	private readonly Room _room;
	private readonly ImmutableArray<RoomSlot> _slots;

	/// <summary>Creates the slots of a room, all open.</summary>
	/// <param name="room">The room the slots belong to.</param>
	internal RoomSlots(Room room)
	{
		_room = room;
		_slots = [.. Enumerable.Range(1, RoomRules.MaxPlayers).Select(index => new RoomSlot(this, index))];
	}

	/// <summary>Gets whether players are barred from changing slot on their own.</summary>
	public bool Locked { get; internal set; }

	/// <summary>Gets the slot a user occupies, or <see langword="null"/> when the user holds none.</summary>
	/// <param name="user">The user.</param>
	public RoomSlot? Find(UserRecord user)
	{
		return _slots.FirstOrDefault(s => user.Equals(s.Player));
	}

	/// <summary>Returns every playing or finished slot to not ready.</summary>
	internal void ResetPlayerStatus()
	{
		foreach (var slot in this.Where(s => s.Status is RoomSlotStatus.Playing or RoomSlotStatus.Complete))
			slot.SetStatus(RoomSlotStatus.NotReady);
	}

	/// <summary>Makes the given number of slots usable, counting seated players, by locking or opening empty slots.</summary>
	/// <param name="size">The number of usable slots, from <see cref="RoomRules.MinPlayers"/> to <see cref="RoomRules.MaxPlayers"/>.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is outside that range.</exception>
	internal void Resize(int size)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(size, RoomRules.MinPlayers);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(size, RoomRules.MaxPlayers);

		// Players may sit in any slot, so the size counts usable slots rather than naming a slot
		// range: keep (size - players) empty slots open and lock the remaining empty ones.
		var open = size - this.Count(s => s.Player is not null);
		foreach (var slot in this.Where(s => s.Player is null))
			slot.Locked = open-- <= 0;
	}

	/// <summary>Moves a player to an open slot of their choice; refused when the room is closed, the player holds no slot, the slots are locked, a round is in progress, or the target is missing, locked or taken.</summary>
	/// <param name="player">The player moving.</param>
	/// <param name="index">The 1-based index of the target slot.</param>
	/// <returns>The outcome and the move made, or <see langword="null"/> when the player already holds that slot.</returns>
	public (RoomActionResult Outcome, (int From, int To)? Move) ChangeSlot(UserRecord player, int index)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, null);
		if (Find(player) is not { } from) return (RoomActionResult.NotInRoom, null);
		if (Locked) return (RoomActionResult.RoomLocked, null);
		if (_room.Rounds.InProgress) return (RoomActionResult.InProgress, null);
		if (SlotAt(index) is not { } to) return (RoomActionResult.SlotNotOpen, null);
		if (ReferenceEquals(from, to)) return (RoomActionResult.Ok, null);
		if (to.Locked || to.Player is not null) return (RoomActionResult.SlotNotOpen, null);

		from.MoveTo(to);
		return (RoomActionResult.Ok, (from.Index, to.Index));
	}

	/// <summary>Moves a player to an open slot regardless of the slot lock and of a round in progress; refused when the room is closed, the player holds no slot, or the target is missing, locked or taken.</summary>
	/// <param name="player">The player to move.</param>
	/// <param name="index">The 1-based index of the target slot.</param>
	/// <returns>The outcome and the move made, or <see langword="null"/> when the player already holds that slot.</returns>
	public (RoomActionResult Outcome, (int From, int To)? Move) MovePlayer(UserRecord player, int index)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, null);
		if (Find(player) is not { } from) return (RoomActionResult.NotInRoom, null);
		if (SlotAt(index) is not { } to) return (RoomActionResult.SlotNotOpen, null);
		if (ReferenceEquals(from, to)) return (RoomActionResult.Ok, null);
		if (to.Locked || to.Player is not null) return (RoomActionResult.SlotNotOpen, null);

		from.MoveTo(to);
		return (RoomActionResult.Ok, (from.Index, to.Index));
	}

	/// <summary>Rearranges every slot at once; refused when the room is closed or a round is in progress.</summary>
	/// <remarks>Slots the arrangement does not mention end up empty and open. Each seated player keeps their status, mods and team.</remarks>
	/// <param name="arrangement">What each named slot holds.</param>
	/// <exception cref="InvalidRoomSettingsException">A slot index is out of range or repeated, a slot with a player is locked, or the players named are not exactly the seated players.</exception>
	public RoomActionResult ArrangeSlots(IReadOnlyList<SlotArrangement> arrangement)
	{
		if (_room.IsClosed) return RoomActionResult.RoomClosed;
		if (_room.Rounds.InProgress) return RoomActionResult.InProgress;

		var seated = this.Where(s => s.Player is not null)
			.ToDictionary(s => s.Player!, s => s.Capture());
		var named = arrangement.Where(e => e.Player is not null).Select(e => e.Player!).ToList();
		if (!IsValidArrangement(arrangement, seated, named)) throw new InvalidRoomSettingsException();

		ApplyArrangement(arrangement, seated);
		return RoomActionResult.Ok;
	}

	private static bool IsValidArrangement(IReadOnlyList<SlotArrangement> arrangement,
		Dictionary<UserRecord, RoomSlotState> seated, List<UserRecord> named)
	{
		return !(arrangement.Any(e =>
			         e.Index is < 1 or > RoomRules.MaxPlayers || (e.Player is not null && e.Locked)) ||
		         arrangement.DistinctBy(e => e.Index).Count() != arrangement.Count ||
		         named.Count != seated.Count || !seated.Keys.ToHashSet().SetEquals(named));
	}

	private void ApplyArrangement(IReadOnlyList<SlotArrangement> arrangement,
		Dictionary<UserRecord, RoomSlotState> seated)
	{
		foreach (var slot in this)
			slot.Clear();

		foreach (var entry in arrangement)
		{
			var slot = this[entry.Index];
			slot.Locked = entry.Locked;
			if (entry.Player is null) continue;

			slot.Restore(seated[entry.Player]);
			if (entry.Team is { } team && _room.Settings.Editable.TeamType.IsTeamMode()) slot.Team = team;
		}
	}

	/// <summary>Sets whether a player is ready; refused when the room is closed, the player holds no slot or a round is in progress.</summary>
	/// <param name="player">The player.</param>
	/// <param name="ready">Whether the player is ready.</param>
	/// <returns>The outcome and the player's slot index, or <see langword="null"/> when the status did not change.</returns>
	public (RoomActionResult Outcome, int? Changed) SetReady(UserRecord player, bool ready)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, null);
		if (Find(player) is not { } slot) return (RoomActionResult.NotInRoom, null);
		if (_room.Rounds.InProgress) return (RoomActionResult.InProgress, null);

		var status = ready ? RoomSlotStatus.Ready : RoomSlotStatus.NotReady;
		if (slot.Status == status) return (RoomActionResult.Ok, null);

		slot.SetStatus(status);
		return (RoomActionResult.Ok, slot.Index);
	}

	/// <summary>Sets whether a player has the beatmap; refused when the room is closed or the player holds no slot.</summary>
	/// <remarks>A playing player is left alone, and a player who has the map is only changed when marked as lacking it.</remarks>
	/// <param name="player">The player.</param>
	/// <param name="has">Whether the player has the beatmap.</param>
	/// <returns>The outcome and the player's slot index, or <see langword="null"/> when the status did not change.</returns>
	public (RoomActionResult Outcome, int? Changed) SetHasMap(UserRecord player, bool has)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, null);
		if (Find(player) is not { } slot) return (RoomActionResult.NotInRoom, null);
		if (slot.Status is RoomSlotStatus.Playing || (has && slot.Status is not RoomSlotStatus.NoMap))
			return (RoomActionResult.Ok, null);

		var status = has ? RoomSlotStatus.NotReady : RoomSlotStatus.NoMap;
		if (slot.Status == status) return (RoomActionResult.Ok, null);

		slot.SetStatus(status);
		return (RoomActionResult.Ok, slot.Index);
	}

	/// <summary>Switches a player to the other team; refused when the room is closed, the player holds no slot, the room is not in a team mode, the slots are locked or a round is in progress.</summary>
	/// <param name="player">The player.</param>
	/// <returns>The outcome, the player's slot index and the new team.</returns>
	public (RoomActionResult Outcome, int Slot, GameTeam Team) ToggleTeam(UserRecord player)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, 0, default);
		if (Find(player) is not { } slot) return (RoomActionResult.NotInRoom, 0, default);
		if (!_room.Settings.Editable.TeamType.IsTeamMode()) return (RoomActionResult.NoTeams, 0, default);
		if (Locked) return (RoomActionResult.RoomLocked, 0, default);
		if (_room.Rounds.InProgress) return (RoomActionResult.InProgress, 0, default);

		var team = slot.Team is GameTeam.Red ? GameTeam.Blue : GameTeam.Red;
		slot.Team = team;
		return (RoomActionResult.Ok, slot.Index, team);
	}

	/// <summary>Puts a player on a team; refused when the room is closed, the room is not in a team mode or the player holds no slot.</summary>
	/// <param name="player">The player.</param>
	/// <param name="team">The team.</param>
	/// <returns>The outcome and the player's slot index, or <see langword="null"/> when the team did not change.</returns>
	public (RoomActionResult Outcome, int? Changed) SetTeam(UserRecord player, GameTeam team)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, null);
		if (!_room.Settings.Editable.TeamType.IsTeamMode()) return (RoomActionResult.NoTeams, null);
		if (Find(player) is not { Player: not null } slot) return (RoomActionResult.NotInRoom, null);
		if (slot.Team == team) return (RoomActionResult.Ok, null);

		slot.Team = team;
		return (RoomActionResult.Ok, slot.Index);
	}

	/// <summary>Sets the mods of a player; refused when the room is closed, the player holds no slot, a round is in progress, the player is ready, the room is not in freemods or the mods include a speed-changing one.</summary>
	/// <param name="player">The player.</param>
	/// <param name="mods">The mods the player chose.</param>
	/// <returns>The outcome and the player's slot index, or <see langword="null"/> when the mods did not change.</returns>
	/// <exception cref="InvalidModsException">The mods are not valid for the room's mode.</exception>
	public (RoomActionResult Outcome, int? Changed) SetMods(UserRecord player, GameMods mods)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, null);
		if (Find(player) is not { } slot) return (RoomActionResult.NotInRoom, null);
		if (_room.Rounds.InProgress) return (RoomActionResult.InProgress, null);
		if (slot.Status is RoomSlotStatus.Ready) return (RoomActionResult.PlayerReady, null);
		if (!_room.Settings.Editable.Freemods) return (RoomActionResult.NotFreemod, null);
		if ((mods & GameModsGroups.SpeedChanging) != GameMods.NoMod) return (RoomActionResult.SpeedModNotAllowed, null);
		if (!mods.IsValid(_room.Settings.Editable.Mode)) throw new InvalidModsException();
		if (slot.Mods == mods) return (RoomActionResult.Ok, null);

		slot.Mods = mods;
		return (RoomActionResult.Ok, slot.Index);
	}

	/// <summary>Locks or unlocks the room's slots against players changing slot; refused when the room is closed.</summary>
	/// <param name="locked">Whether the slots are locked.</param>
	/// <returns>The outcome and whether the lock changed.</returns>
	public (RoomActionResult Outcome, bool Changed) SetLocked(bool locked)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, false);
		if (Locked == locked) return (RoomActionResult.Ok, false);

		Locked = locked;
		return (RoomActionResult.Ok, true);
	}

	/// <summary>Toggles the lock of one slot, evicting its player when it locks; refused when the room is closed, the slot does not exist or the actor holds the slot.</summary>
	/// <param name="actor">The user acting, who cannot lock their own slot; <see langword="null"/> when no user acts.</param>
	/// <param name="index">The 1-based index of the slot.</param>
	/// <param name="now">The time of the change.</param>
	/// <returns>The outcome, the slot index, whether the slot is now locked, the player evicted if any, and how the round in progress moved on, or <see langword="null"/> when it did not.</returns>
	public (RoomActionResult Outcome, int Slot, bool Locked, UserRecord? Evicted, RoomRoundProgress? Progress) LockSlot(
		UserRecord? actor, int index, DateTimeOffset now)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, 0, false, null, null);
		if (SlotAt(index) is not { } slot) return (RoomActionResult.SlotNotOpen, 0, false, null, null);
		if (actor is not null && actor.Equals(slot.Player)) return (RoomActionResult.OwnSlot, 0, false, null, null);

		var locking = !slot.Locked;

		UserRecord? evicted = null;
		RoomRoundProgress? progress = null;
		if (locking && slot.Player is { } player)
		{
			evicted = player;
			Vacate(player);
			progress = _room.Rounds.Advance(now);
		}

		slot.Locked = locking;
		return (RoomActionResult.Ok, slot.Index, locking, evicted, progress);
	}

	/// <summary>Seats a player in the first open slot and gives them the player role; refused when the room is closed, the player is banned or no slot is open.</summary>
	/// <remarks>A player who already holds a slot keeps it. In a team mode the player joins the red team. A room without a host makes the seated player its host.</remarks>
	/// <param name="player">The player to seat.</param>
	/// <returns>The outcome and the slot the player holds.</returns>
	public (RoomActionResult Outcome, RoomSlot? Slot) Seat(UserRecord player)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, null);
		if (_room.Members.Banned.Contains(player)) return (RoomActionResult.Banned, null);
		if (Find(player) is { } existing) return (RoomActionResult.Ok, existing);

		var slot = this.FirstOrDefault(s => s is { Locked: false, Player: null });
		if (slot is null) return (RoomActionResult.Full, null);

		slot.Occupy(player);
		_room.Members.Grant(_room.Members.Of(player), RoomPrivileges.Player);
		if (_room.Members.Host is null) _room.Members.Grant(_room.Members.Of(player), RoomPrivileges.Host);

		if (_room.Settings.Editable.TeamType.IsTeamMode()) slot.Team = GameTeam.Red;

		return (RoomActionResult.Ok, slot);
	}

	/// <summary>Seats a player again in the slot they already hold, as when a new connection replaces an old one; refused when the room is closed or the player holds no slot.</summary>
	/// <param name="player">The player.</param>
	/// <param name="now">The time of the change.</param>
	/// <returns>The outcome, the slot index, and how the round in progress moved on, or <see langword="null"/> when it did not.</returns>
	public (RoomActionResult Outcome, int Slot, RoomRoundProgress? Progress) Reseat(UserRecord player, DateTimeOffset now)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, 0, null);
		if (Find(player) is not { } slot) return (RoomActionResult.NotInRoom, 0, null);

		var team = slot.Team;
		slot.Clear();
		slot.Occupy(player);
		slot.Team = team;
		return (RoomActionResult.Ok, slot.Index, _room.Rounds.Advance(now));
	}

	/// <summary>Takes a player out of their slot of their own will; refused when the room is closed or the player holds no slot.</summary>
	/// <param name="player">The player leaving.</param>
	/// <param name="now">The time of the change.</param>
	/// <returns>The outcome, the vacated slot index, and how the round in progress moved on, or <see langword="null"/> when it did not.</returns>
	public (RoomActionResult Outcome, int Slot, RoomRoundProgress? Progress) Leave(UserRecord player, DateTimeOffset now)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, 0, null);
		if (Vacate(player) is not { } slot) return (RoomActionResult.NotInRoom, 0, null);

		return (RoomActionResult.Ok, slot.Index, _room.Rounds.Advance(now));
	}

	/// <summary>Empties the slot of a player, handing the host role on when the player was the host and striking the player from the settlement of the round in progress.</summary>
	/// <param name="player">The player.</param>
	/// <returns>The emptied slot, or <see langword="null"/> when the player held none.</returns>
	internal RoomSlot? Vacate(UserRecord player)
	{
		var slot = Find(player);
		if (slot is null) return null;

		LeaveSeat(player);

		_room.Rounds.Settlement?.Leave(player);
		slot.Clear();
		return slot;
	}

	private void LeaveSeat(UserRecord leaving)
	{
		var wasHost = leaving.Equals(_room.Members.Host?.Record);
		_room.Members.Depart(leaving);
		if (!wasHost) return;

		var next = this.FirstOrDefault(slot => slot.Player is not null && !slot.Player.Equals(leaving))?.Player;
		if (next is not null) _room.Members.Grant(_room.Members.Of(next), RoomPrivileges.Host);
	}

	private RoomSlot? SlotAt(int index)
	{
		return index is >= 1 and <= RoomRules.MaxPlayers ? this[index] : null;
	}

	public int Count => _slots.Length;

	/// <summary>Gets a slot by its 1-based index.</summary>
	/// <param name="index">The 1-based index of the slot.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside 1 to <see cref="RoomRules.MaxPlayers"/>.</exception>
	public RoomSlot this[int index]
	{
		get
		{
			ThrowIfOutOfRangeIndex(index);
			return _slots[index - 1];
		}
	}

	public IEnumerator<RoomSlot> GetEnumerator()
	{
		return ((IEnumerable<RoomSlot>)_slots).GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	private static void ThrowIfOutOfRangeIndex(int index)
	{
		if (index is < 1 or > RoomRules.MaxPlayers)
			throw new ArgumentOutOfRangeException(nameof(index), index,
				$"Slot index must be between 1 and {RoomRules.MaxPlayers}.");
	}
}