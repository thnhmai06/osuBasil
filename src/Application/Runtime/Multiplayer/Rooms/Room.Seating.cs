using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Application.Runtime.Sessions;
using Basil.Domain.Mechanics;
using Basil.Domain.Permissions;

namespace Basil.Application.Runtime.Multiplayer.Rooms;

/// <content>Seating, leaving and observing.</content>
public sealed partial class Room
{
	/// <summary>Seats a player in the lowest-index empty, unlocked slot.</summary>
	/// <param name="player">The connection to seat.</param>
	/// <returns>The assigned slot, or <see langword="null" /> when no slot is available.</returns>
	/// <remarks>Seating a player who already has a slot returns that slot. The caller reports the seating.</remarks>
	/// <exception cref="InvalidOperationException">The user is banned from the room.</exception>
	internal RoomSlot? Seat(BanchoConnection player)
	{
		if (Members.Banned.Contains(player.Record))
			throw new InvalidOperationException("The user is banned from this room.");

		if (Slots.Find(player) is { } existing) return existing;

		var slot = Slots.FirstOrDefault(s => s is { Locked: false, Player: null });
		if (slot is null) return null;

		slot.Occupy(player);

		if (Settings.MatchSettings.TeamType.IsTeamMode())
		{
			var redCount = Slots.Count(s => s.Team == GameTeam.Red);
			var blueCount = Slots.Count(s => s.Team == GameTeam.Blue);
			slot.Team = redCount <= blueCount ? GameTeam.Red : GameTeam.Blue;
		}

		return slot;
	}

	/// <summary>Seats a player who passed the room's checks, replacing a closed connection of the same user if present.</summary>
	/// <param name="player">The connection to seat.</param>
	/// <param name="stale">The closed connection whose seat the player may take.</param>
	/// <returns>The result of seating the player.</returns>
	internal RoomOutcome TakeSeat(BanchoConnection player, BanchoConnection? stale)
	{
		if (stale is not null) return ReplaceSeat(player, stale);

		if (Seat(player) is not { } slot) return RoomOutcome.Full;

		JoinChannel(player);
		MarkOccupied();
		Context.Events.Publish(new RoomPlayerJoined(this, player, slot.Index, null, null));
		return RoomOutcome.Ok;
	}

	/// <summary>
	///     Checks whether a connection takes part in the room: seated, observing, managing it while allowed to join rooms,
	///     or managing every room.
	/// </summary>
	/// <param name="connection">The connection to check.</param>
	/// <param name="now">The moment to evaluate restrictions at.</param>
	internal bool Involves(Connection connection, DateTimeOffset now)
	{
		var permissions = connection.User.EffectivePermissions(now);

		return (connection is BanchoConnection player && Slots.Find(player) is not null)
		       || (connection is TourneyConnection observer && Members.Observers.Contains(observer))
		       || (Authority.IsManagedBy(connection.Record) && permissions.Allows(Permissions.RoomJoin))
		       || permissions.Allows(Permissions.RoomManageAny);
	}

	/// <summary>Removes a seated player and updates the room's round, channel and empty-room state.</summary>
	/// <param name="by">The player's game connection.</param>
	/// <returns>Ok when the player left, otherwise NotInRoom.</returns>
	internal RoomOutcome Leave(BanchoConnection by)
	{
		if (Vacate(by) is not { } slot) return RoomOutcome.NotInRoom;

		var progress = AdvanceRound();
		Context.Events.Publish(new RoomPlayerLeft(this, by, slot.Index, Authority.Host, progress));
		LeaveChannel(by);
		ReportIfEmpty();
		return RoomOutcome.Ok;
	}

	/// <summary>Adds a tournament connection as an observer.</summary>
	/// <param name="by">The tournament connection observing.</param>
	/// <returns>Ok, or IsPlayer when the user is seated in the room.</returns>
	internal RoomOutcome ObserverJoin(TourneyConnection by)
	{
		if (Slots.Find(by.Record) is not null) return RoomOutcome.IsPlayer;
		if (!Members.MutableObservers.Add(by)) return RoomOutcome.Ok;

		Context.Events.Publish(new RoomObserverJoined(this, by));
		JoinChannel(by);
		return RoomOutcome.Ok;
	}

	/// <summary>Removes a tournament connection from the room's observers.</summary>
	/// <param name="by">The tournament connection leaving.</param>
	/// <returns>Ok when the observer left, otherwise NotObserver.</returns>
	internal RoomOutcome ObserverLeave(TourneyConnection by)
	{
		if (!Members.MutableObservers.Remove(by)) return RoomOutcome.NotObserver;

		Context.Events.Publish(new RoomObserverLeft(this, by));
		LeaveChannel(by);
		return RoomOutcome.Ok;
	}

	/// <summary>Clears the slot occupied by a player, passing host on when the player was host.</summary>
	/// <param name="player">The connection to remove.</param>
	/// <returns>The slot that was vacated, or <see langword="null" /> when the connection was not seated.</returns>
	/// <remarks>When the host leaves, host passes to the next seated player by slot order.</remarks>
	internal RoomSlot? Vacate(BanchoConnection player)
	{
		var slot = Slots.Find(player);
		if (slot is null) return null;

		PassHostFrom(player);

		slot.Clear();
		return slot;
	}

	/// <summary>Finds the room a player is seated in.</summary>
	/// <param name="player">The player's game connection.</param>
	/// <returns>The room containing the player, or <see langword="null" />.</returns>
	internal Room? RoomOf(BanchoConnection player)
	{
		return Context.Lobby.RoomOf(player);
	}

	/// <summary>Adds a connection to this room's channel.</summary>
	/// <param name="connection">The connection that joins.</param>
	internal void JoinChannel(Connection connection)
	{
		Channel.ScopeTo(connection).Join();
	}

	/// <summary>Removes a connection from this room's channel unless its user manages the room.</summary>
	/// <param name="connection">The connection that leaves.</param>
	internal void LeaveChannel(Connection connection)
	{
		if (!Authority.IsManagedBy(connection.Record)) Channel.ScopeTo(connection).Part();
	}

	/// <summary>Tells the lobby that this room has a player again.</summary>
	internal void MarkOccupied()
	{
		Context.Lobby.Occupied(this);
	}

	/// <summary>Tells the lobby when this room has no seated players.</summary>
	internal void ReportIfEmpty()
	{
		if (!Slots.Any(slot => slot.Player is not null)) Context.Lobby.Emptied(this);
	}

	private void PassHostFrom(BanchoConnection leaving)
	{
		if (!ReferenceEquals(Authority.Host, leaving)) return;
		Authority.Host = Slots
			.FirstOrDefault(slot => slot.Player is not null && !ReferenceEquals(slot.Player, leaving))
			?.Player;
	}

	/// <summary>Gives the seat of a closed connection to a new connection of the same user.</summary>
	/// <param name="player">The connection taking the seat.</param>
	/// <param name="stale">The closed connection whose seat is replaced.</param>
	/// <returns>Ok after replacing the seated connection.</returns>
	private RoomOutcome ReplaceSeat(BanchoConnection player, BanchoConnection stale)
	{
		var slot = Slots.Find(stale)!;
		var team = slot.Team;
		var wasHost = ReferenceEquals(Authority.Host, stale);

		slot.Clear();
		slot.Occupy(player);
		slot.Team = team;
		if (wasHost) Authority.Host = player;

		LeaveChannel(stale);
		var progress = AdvanceRound();
		JoinChannel(player);
		MarkOccupied();
		Context.Events.Publish(new RoomPlayerJoined(this, player, slot.Index, stale, progress));
		return RoomOutcome.Ok;
	}
}
