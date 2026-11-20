using System.Collections;
using System.Collections.Immutable;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer.Room;

public sealed class RoomSlots : IReadOnlyList<RoomSlot>
{
	private readonly Room _room;
	private readonly ImmutableArray<RoomSlot> _slots;

	internal RoomSlots(Room room)
	{
		_room = room;
		_slots = [.. Enumerable.Range(1, RoomRules.MaxPlayers).Select(index => new RoomSlot(this, index))];
	}

	public bool Locked { get; internal set; }

	public RoomSlot? Find(UserRecord user)
	{
		return _slots.FirstOrDefault(s => user.Equals(s.Player));
	}

	internal void ResetPlayerStatus()
	{
		foreach (var slot in this.Where(s => s.Status is RoomSlotStatus.Playing or RoomSlotStatus.Complete))
			slot.SetStatus(RoomSlotStatus.NotReady);
	}

	internal void Resize(int size)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(size, RoomRules.MaxPlayers);

		// Players may sit in any slot, so the size counts usable slots rather than naming a slot
		// range: keep (size - players) empty slots open and lock the remaining empty ones.
		var open = size - this.Count(s => s.Player is not null);
		foreach (var slot in this.Where(s => s.Player is null))
			slot.Locked = open-- <= 0;
	}

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

	public (RoomActionResult Outcome, int? Changed) SetTeam(UserRecord player, GameTeam team)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, null);
		if (!_room.Settings.Editable.TeamType.IsTeamMode()) return (RoomActionResult.NoTeams, null);
		if (Find(player) is not { Player: not null } slot) return (RoomActionResult.NotInRoom, null);
		if (slot.Team == team) return (RoomActionResult.Ok, null);

		slot.Team = team;
		return (RoomActionResult.Ok, slot.Index);
	}

	public (RoomActionResult Outcome, int? Changed) SetMods(UserRecord player, GameMods mods)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, null);
		if (Find(player) is not { } slot) return (RoomActionResult.NotInRoom, null);
		if (_room.Rounds.InProgress) return (RoomActionResult.InProgress, null);
		if (!_room.Settings.Editable.Freemods) return (RoomActionResult.NotFreemod, null);
		if ((mods & GameModsGroups.SpeedChanging) != GameMods.NoMod) return (RoomActionResult.SpeedModNotAllowed, null);
		if (!mods.IsValid(_room.Settings.Editable.Mode)) throw new InvalidModsException();
		if (slot.Mods == mods) return (RoomActionResult.Ok, null);

		slot.Mods = mods;
		return (RoomActionResult.Ok, slot.Index);
	}

	public (RoomActionResult Outcome, bool Changed) SetLocked(bool locked)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, false);
		if (Locked == locked) return (RoomActionResult.Ok, false);

		Locked = locked;
		return (RoomActionResult.Ok, true);
	}

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

	public (RoomActionResult Outcome, RoomSlot? Slot) Seat(UserRecord player)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, null);
		if (_room.Members.Banned.Contains(player)) return (RoomActionResult.Banned, null);
		if (Find(player) is { } existing) return (RoomActionResult.Ok, existing);

		var slot = this.FirstOrDefault(s => s is { Locked: false, Player: null });
		if (slot is null) return (RoomActionResult.Full, null);

		slot.Occupy(player);
		_room.Members.Grant(_room.Members.Of(player), RoomPrivileges.Player);

		if (_room.Settings.Editable.TeamType.IsTeamMode())
		{
			var redCount = this.Count(s => s.Team == GameTeam.Red);
			var blueCount = this.Count(s => s.Team == GameTeam.Blue);
			slot.Team = redCount <= blueCount ? GameTeam.Red : GameTeam.Blue;
		}

		return (RoomActionResult.Ok, slot);
	}

	/// <summary>Seats a player again in the slot they already hold, as when a new connection replaces an old one.</summary>
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

	/// <summary>Takes a player out of their slot of their own will, and reports how the round in progress moved on.</summary>
	public (RoomActionResult Outcome, int Slot, RoomRoundProgress? Progress) Leave(UserRecord player, DateTimeOffset now)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, 0, null);
		if (Vacate(player) is not { } slot) return (RoomActionResult.NotInRoom, 0, null);

		return (RoomActionResult.Ok, slot.Index, _room.Rounds.Advance(now));
	}

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