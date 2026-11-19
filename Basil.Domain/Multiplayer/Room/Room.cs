using Basil.Domain.Multiplayer.Report.Matches;

namespace Basil.Domain.Multiplayer.Room;

/// <summary>A multiplayer room: its settings, slots, roles and rounds, with every rule that keeps them consistent.</summary>
public sealed class Room
{
	public Room(MatchRecord match, MatchSettings settings, string password = "")
	{
		Match = match;
		Settings = new RoomSettings(this, match, settings, password);
		Rounds = new RoomRounds(this);
		Slots = new RoomSlots(this);
		Members = new RoomMembers(this);
	}

	public MatchRecord Match { get; }

	public RoomSlots Slots { get; }

	public RoomMembers Members { get; }

	public RoomSettings Settings { get; }

	public RoomRounds Rounds { get; }

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
