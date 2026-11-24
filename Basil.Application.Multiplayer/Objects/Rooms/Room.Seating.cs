using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Multiplayer.Room;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed partial class Room
{
	internal (RoomActionResult Outcome, RoomSlot? Slot) Seat(StableSession player)
	{
		var added = _seats.TryAdd(player.Record, player);
		var seat = State.Slots.Seat(player.Record);
		if (seat.Slot is null && added) Unseat(player.Record);
		return seat;
	}

	internal RoomJoinResult TakeSeat(StableSession player, StableSession? stale)
	{
		if (stale is not null) return ReplaceSeat(player, stale);

		var hadHost = State.Members.Host is not null;
		var (outcome, slot) = Seat(player);
		if (slot is null) return RoomJoinResults.Of(outcome);

		MarkOccupied();
		Emit(new RoomPlayerJoined(this, player, slot.Index, null, null));
		// A room without a host hands it to the player who takes a seat.
		if (!hadHost) Emit(new RoomHostChanged(this, player.Record, player));
		return RoomJoinResult.Ok;
	}

	public bool Involves(Session connection, UserPrivileges privileges)
	{
		return State.Involves(connection.Record, privileges);
	}

	public bool CanBeViewedBy(Session actor)
	{
		return State.CanBeViewedBy(actor.Record, actor.EffectivePrivileges(Now));
	}

	private RoomActionResult LeaveSeat(StableSession actor)
	{
		if (Seated(actor) is not { } record) return RoomActionResult.NotInRoom;

		var wasHost = ReferenceEquals(Host, actor);
		var (outcome, slot, progress) = State.Slots.Leave(record, Now);
		if (outcome is not RoomActionResult.Ok) return outcome;

		Unseat(record);
		Emit(new RoomPlayerLeft(this, actor, slot, Host, progress));
		if (wasHost && Host is { } next) Emit(new RoomHostChanged(this, record, next));
		if (progress is { Completed: true }) SettleRound();
		ReportIfEmpty();
		return RoomActionResult.Ok;
	}

	internal void Vacate(StableSession player)
	{
		if (Seated(player) is not { } record) return;

		State.Slots.Leave(record, Now);
		Unseat(record);
	}

	internal Room? RoomOf(StableSession player)
	{
		return Context.Lobby.RoomOf(player);
	}

	internal void MarkOccupied()
	{
		Context.Lobby.Occupied(this);
	}

	internal void ReportIfEmpty()
	{
		if (!Slots.Any(slot => slot.Player is not null)) Context.Lobby.Emptied(this);
	}

	private RoomJoinResult ReplaceSeat(StableSession player, StableSession stale)
	{
		var (outcome, slot, progress) = State.Slots.Reseat(player.Record, Now);
		if (outcome is not RoomActionResult.Ok) return RoomJoinResults.Of(outcome);

		_seats[player.Record] = player;
		MarkOccupied();
		Emit(new RoomPlayerJoined(this, player, slot, stale, progress));
		if (progress is { Completed: true }) SettleRound();
		return RoomJoinResult.Ok;
	}
}