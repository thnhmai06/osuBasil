using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Game.Users;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed partial class Room
{
	internal RoomSlot? Seat(StableSession player)
	{
		var added = _seats.TryAdd(player.Record, player);
		var slot = State.Seat(player.Record);
		if (slot is null && added) Unseat(player.Record);
		return slot;
	}

	internal RoomOutcome TakeSeat(StableSession player, StableSession? stale)
	{
		if (stale is not null) return ReplaceSeat(player, stale);

		if (Seat(player) is not { } slot) return RoomOutcome.Full;

		MarkOccupied();
		Emit(new RoomPlayerJoined(this, player, slot.Index, null, null));
		return RoomOutcome.Ok;
	}

	public bool IsPresent(Session connection)
	{
		return (connection is StableSession player && SlotOf(player) is not null)
		       || (connection is TourneySession observer && Members.Observers.Contains(observer));
	}

	public bool Involves(Session connection, UserPrivileges privileges)
	{
		return IsPresent(connection)
		       || (Authority.IsManagedBy(connection.Record) && privileges.Allows(UserPrivileges.Player))
		       || privileges.Allows(UserPrivileges.Tournament);
	}

	private RoomOutcome LeaveSeat(StableSession actor)
	{
		if (Vacate(actor) is not { } slot) return RoomOutcome.NotInRoom;

		var progress = AdvanceRound();
		Emit(new RoomPlayerLeft(this, actor, slot.Index, Host, progress));
		ReportIfEmpty();
		return RoomOutcome.Ok;
	}

	internal RoomSlot? Vacate(StableSession player)
	{
		if (Seated(player) is not { } record) return null;

		var slot = State.Vacate(record);
		Unseat(record);
		return slot;
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

	private RoomOutcome ReplaceSeat(StableSession player, StableSession stale)
	{
		_seats[player.Record] = player;
		var slot = State.Reseat(player.Record);

		var progress = AdvanceRound();
		MarkOccupied();
		Emit(new RoomPlayerJoined(this, player, slot.Index, stale, progress));
		return RoomOutcome.Ok;
	}
}
