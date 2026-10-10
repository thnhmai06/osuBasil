using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Multiplayer.Room;
using Basil.Domain.Multiplayer;
using Basil.Domain.Multiplayer.Round;

namespace Basil.Application.Services.Implementations.Multiplayer;

/// <summary>The round and countdown mechanics the lobby and room services share.</summary>
internal static class RoundMechanics
{
	/// <summary>Stops a room's running countdown without announcing it.</summary>
	/// <param name="room">The room whose countdown to stop.</param>
	/// <remarks>Stopping a room that has no countdown does nothing.</remarks>
	internal static void StopCountdown(Room room)
	{
		room.Rounds.CountdownTimer?.Dispose();
		room.Rounds.CountdownTimer = null;
		room.Rounds.CountdownEndsAt = null;
		room.Rounds.CountdownStartsRound = false;
	}

	/// <summary>Ends a room's round in progress and sets its players back to not ready.</summary>
	/// <param name="room">The room whose round to end.</param>
	/// <param name="now">The moment the round ends.</param>
	/// <param name="aborted">Whether the round ends as aborted rather than played out.</param>
	/// <returns>The round that ended, or <see langword="null" /> when no round was in progress.</returns>
	internal static Round? EndCurrentRound(Room room, DateTimeOffset now, bool aborted)
	{
		if (room.Rounds.CurrentRound is not { } round) return null;

		round.EndedAt = now;
		round.Aborted = aborted;
		RoomSlotsMechanics.ResetPlayers(room);
		return round;
	}
}