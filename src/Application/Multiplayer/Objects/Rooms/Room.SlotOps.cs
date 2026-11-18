using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Game.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed partial class Room
{
	internal RoomOutcome ChangeSlot(StableSession stable, int index)
	{
		using (State.Enter())
		{
			if (Seated(stable) is not { } player) return IsClosed ? RoomOutcome.RoomClosed : RoomOutcome.NotInRoom;

			var (outcome, move) = State.ChangeSlot(player, index);
			if (move is { } moved) Emit(new RoomPlayerMoved(this, stable, moved.From, moved.To));
			return outcome;
		}
	}

	internal RoomOutcome MovePlayer(UserRecord player, int index)
	{
		using (State.Enter())
		{
			var (outcome, move) = State.MovePlayer(player, index);
			if (move is { } moved) Emit(new RoomPlayerMoved(this, SessionOf(player)!, moved.From, moved.To));
			return outcome;
		}
	}

	internal RoomOutcome ArrangeSlots(IReadOnlyList<SlotArrangement> arrangement)
	{
		using (State.Enter())
		{
			var outcome = State.ArrangeSlots(arrangement);
			if (outcome is RoomOutcome.Ok) Emit(new RoomSlotsArranged(this));
			return outcome;
		}
	}

	internal RoomOutcome SetReady(StableSession stable, bool ready)
	{
		using (State.Enter())
		{
			if (Seated(stable) is not { } player) return IsClosed ? RoomOutcome.RoomClosed : RoomOutcome.NotInRoom;

			var (outcome, changed) = State.SetReady(player, ready);
			if (changed is { } slot)
				Emit(new RoomSlotStatusChanged(this, slot, ready ? RoomSlotStatus.Ready : RoomSlotStatus.NotReady));
			return outcome;
		}
	}

	internal RoomOutcome SetHasMap(StableSession stable, bool has)
	{
		using (State.Enter())
		{
			if (Seated(stable) is not { } player) return IsClosed ? RoomOutcome.RoomClosed : RoomOutcome.NotInRoom;

			var (outcome, changed) = State.SetHasMap(player, has);
			if (changed is { } slot)
				Emit(new RoomSlotStatusChanged(this, slot, has ? RoomSlotStatus.NotReady : RoomSlotStatus.NoMap));
			return outcome;
		}
	}

	internal RoomOutcome ToggleTeam(StableSession stable)
	{
		using (State.Enter())
		{
			if (Seated(stable) is not { } player) return IsClosed ? RoomOutcome.RoomClosed : RoomOutcome.NotInRoom;

			var (outcome, slot, team) = State.ToggleTeam(player);
			if (outcome is RoomOutcome.Ok) Emit(new RoomSlotTeamChanged(this, slot, team));
			return outcome;
		}
	}

	internal RoomOutcome SetTeam(UserRecord player, GameTeam team)
	{
		using (State.Enter())
		{
			var (outcome, changed) = State.SetTeam(player, team);
			if (changed is { } slot) Emit(new RoomSlotTeamChanged(this, slot, team));
			return outcome;
		}
	}

	internal RoomOutcome SetMods(StableSession stable, GameMods mods)
	{
		using (State.Enter())
		{
			if (Seated(stable) is not { } player) return IsClosed ? RoomOutcome.RoomClosed : RoomOutcome.NotInRoom;

			var (outcome, changed) = State.SetMods(player, mods);
			if (changed is { } slot) Emit(new RoomSlotModsChanged(this, slot, mods));
			return outcome;
		}
	}

	internal RoomOutcome ToggleSlotLock(Session actor, int index)
	{
		using (State.Enter())
		{
			var (outcome, slot, locking, evicted, progress) = State.LockSlot(PlayerOf(actor), index, Now);
			if (outcome is not RoomOutcome.Ok) return outcome;

			var session = evicted is null ? null : Unseat(evicted);
			Emit(new RoomSlotLockChanged(this, slot, locking, session, Host, progress));

			if (session is not null) ReportIfEmpty();

			return RoomOutcome.Ok;
		}
	}
}
