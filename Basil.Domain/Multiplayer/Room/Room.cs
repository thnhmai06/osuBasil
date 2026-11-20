using Basil.Domain.Multiplayer.Report.Matches;

namespace Basil.Domain.Multiplayer.Room;

/// <summary>A multiplayer room: its settings, slots, roles and rounds, with every rule that keeps them consistent.</summary>
public sealed class Room
{
	/// <summary>Creates an open room for a match.</summary>
	/// <param name="match">The match the room hosts; its creator becomes the room's creator.</param>
	/// <param name="settings">The settings the room starts with.</param>
	/// <param name="password">The password players must give to join; empty for none.</param>
	public Room(MatchRecord match, MatchSettings settings, string password = "")
	{
		Match = match;
		Settings = new RoomSettings(this, match, settings, password);
		Rounds = new RoomRounds(this);
		Slots = new RoomSlots(this);
		Members = new RoomMembers(this);
	}

	/// <summary>Gets the match the room hosts.</summary>
	public MatchRecord Match { get; }

	/// <summary>Gets the slots of the room.</summary>
	public RoomSlots Slots { get; }

	/// <summary>Gets everyone taking part in the room and the users banned from it.</summary>
	public RoomMembers Members { get; }

	/// <summary>Gets the settings of the room.</summary>
	public RoomSettings Settings { get; }

	/// <summary>Gets the rounds played in the room.</summary>
	public RoomRounds Rounds { get; }

	/// <summary>Gets whether the match has ended; a closed room refuses every action with <see cref="RoomActionResult.RoomClosed"/>.</summary>
	public bool IsClosed => Match.Value.EndedAt is not null;

	/// <summary>Closes the room, ending the round in progress as aborted; refused when it is already closed.</summary>
	public RoomActionResult Close(DateTimeOffset now)
	{
		if (IsClosed) return RoomActionResult.RoomClosed;

		Rounds.EndCurrentRound(true, now);
		Match.Value.EndedAt = now;
		return RoomActionResult.Ok;
	}
}
