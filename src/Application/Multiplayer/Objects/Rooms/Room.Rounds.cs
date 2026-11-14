using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Multiplayer.Objects.Rooms;

/// <content>The round lifecycle.</content>
public sealed partial class Room
{
	/// <summary>Runs the work of a timer so that a fault in it cannot end the process.</summary>
	/// <param name="action">The work.</param>
	internal static void Fire(Action action)
	{
		try
		{
			action();
		}
		catch (Exception)
		{
			// A timer has no caller to report to; the failure is counted.
			RoomMetrics.TimerFailed.Add(1);
		}
	}

	/// <summary>Ends a room's round in progress and sets its players back to not ready.</summary>
	/// <param name="aborted">Whether the round ends as aborted rather than played out.</param>
	/// <returns>The round that ended, or <see langword="null" /> when no round was in progress.</returns>
	internal Round? EndCurrentRound(bool aborted)
	{
		if (Rounds.CurrentRound is not { } round) return null;

		round.EndedAt = Context.Clock.GetUtcNow();
		round.Aborted = aborted;
		Slots.ResetPlayers();
		if (aborted)
			Rounds.Settlement?.Close();
		else
			_settlePending = true;
		return round;
	}

	/// <summary>Begins a round and marks the seated players who have the beatmap as playing.</summary>
	/// <returns>The new round and the players taking part.</returns>
	internal (Round Round, IReadOnlyList<StableConnection> Players) BeginRound()
	{
		var round = new Round
		{
			Index = (Rounds.LastRound?.Index ?? 0) + 1,
			Match = Match,
			BeatmapHash = Settings.Beatmap!.Hash,
			Settings = Settings.Editable.Clone(),
			StartedAt = Context.Clock.GetUtcNow(),
			EndedAt = null
		};
		Rounds.LastRound = round;
		Rounds.AllLoadedAnnounced = false;
		Rounds.AllSkippedAnnounced = false;

		var players = new List<StableConnection>();
		foreach (var slot in Slots.Where(s => s.Player is not null && s.Status is not RoomSlotStatus.NoMap))
		{
			slot.SetStatus(RoomSlotStatus.Playing);
			players.Add(slot.Player!);
		}

		BeginSettlement(round, players);
		if (players.Count == 0) EndCurrentRound(false);
		return (round, players);
	}

	/// <summary>Works out what the players who are still playing have all done, after a player stopped playing.</summary>
	/// <returns>
	///     What changed for the round in progress, or <see langword="null" /> when no round is in progress or nothing
	///     changed.
	/// </returns>
	/// <remarks>
	///     Ends the round when nobody is playing any more; otherwise reports that every remaining player has loaded or
	///     skipped, once per round.
	/// </remarks>
	internal RoomRoundProgress? AdvanceRound()
	{
		if (Rounds.CurrentRound is not { } round) return null;

		var playing = Slots.Where(s => s.Status is RoomSlotStatus.Playing).ToList();
		if (playing.Count == 0)
		{
			EndCurrentRound(false);
			return new RoomRoundProgress(round, false, false, true);
		}

		var allLoaded = !Rounds.AllLoadedAnnounced && playing.All(s => s.Loaded is true);
		if (allLoaded) Rounds.AllLoadedAnnounced = true;

		var allSkipped = !Rounds.AllSkippedAnnounced && playing.All(s => s.IntroSkipped is true);
		if (allSkipped) Rounds.AllSkippedAnnounced = true;

		return allLoaded || allSkipped ? new RoomRoundProgress(round, allLoaded, allSkipped, false) : null;
	}
}