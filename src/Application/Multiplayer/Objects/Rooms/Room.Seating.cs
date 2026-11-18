using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Game.Mechanics;
using Basil.Domain.Users;
using Basil.Domain.Game.Users;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed partial class Room
{
	internal RoomSlot? Seat(StableSession player)
	{
		if (Members.Banned.Contains(player.Record))
			throw new InvalidOperationException("The user is banned from this room.");

		if (Slots.Find(player) is { } existing) return existing;

		var slot = Slots.FirstOrDefault(s => s is { Locked: false, Player: null });
		if (slot is null) return null;

		slot.Occupy(player);

		if (Settings.Editable.TeamType.IsTeamMode())
		{
			var redCount = Slots.Count(s => s.Team == GameTeam.Red);
			var blueCount = Slots.Count(s => s.Team == GameTeam.Blue);
			slot.Team = redCount <= blueCount ? GameTeam.Red : GameTeam.Blue;
		}

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
		return (connection is StableSession player && Slots.Find(player) is not null)
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
		Emit(new RoomPlayerLeft(this, actor, slot.Index, Authority.Host, progress));
		ReportIfEmpty();
		return RoomOutcome.Ok;
	}

	internal RoomSlot? Vacate(StableSession player)
	{
		var slot = Slots.Find(player);
		if (slot is null) return null;

		PassHostFrom(player);

		Rounds.Settlement?.Leave(player.Record);
		slot.Clear();
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

	private void PassHostFrom(StableSession leaving)
	{
		if (!ReferenceEquals(Authority.Host, leaving)) return;
		Authority.Host = Slots
			.FirstOrDefault(slot => slot.Player is not null && !ReferenceEquals(slot.Player, leaving))
			?.Player;
	}

	private RoomOutcome ReplaceSeat(StableSession player, StableSession stale)
	{
		var slot = Slots.Find(stale)!;
		var team = slot.Team;
		var wasHost = ReferenceEquals(Authority.Host, stale);

		slot.Clear();
		slot.Occupy(player);
		slot.Team = team;
		if (wasHost) Authority.Host = player;

		var progress = AdvanceRound();
		MarkOccupied();
		Emit(new RoomPlayerJoined(this, player, slot.Index, stale, progress));
		return RoomOutcome.Ok;
	}
}