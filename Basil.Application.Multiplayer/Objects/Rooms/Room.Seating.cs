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

		var (outcome, slot) = Seat(player);
		if (slot is null) return RoomJoinResults.Of(outcome);

		MarkOccupied();
		Emit(new RoomPlayerJoined(this, player, slot.Index, null, null));
		return RoomJoinResult.Ok;
	}

	public bool IsPresent(Session connection)
	{
		return (connection is StableSession player && SlotOf(player) is not null)
		       || (connection is TourneySession observer && Members.Observers.Contains(observer));
	}

	public bool Involves(Session connection, UserPrivileges privileges)
	{
		return IsPresent(connection)
		       || (IsManagedBy(connection.Record) && privileges.Allows(UserPrivileges.Player))
		       || privileges.Allows(UserPrivileges.Tournament);
	}

	private RoomActionResult LeaveSeat(StableSession actor)
	{
		if (Seated(actor) is not { } record) return RoomActionResult.NotInRoom;

		var (outcome, slot, progress) = State.Slots.Leave(record, Now);
		if (outcome is not RoomActionResult.Ok) return outcome;

		Unseat(record);
		Emit(new RoomPlayerLeft(this, actor, slot, Host, progress));
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