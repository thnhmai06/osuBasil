using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Application.Runtime.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Multiplayer.Rooms;

/// <content>Countdowns and the round lifecycle.</content>
public sealed partial class Room
{
	/// <summary>Starts a countdown in this room, replacing any countdown already running.</summary>
	/// <param name="by">The user who started the countdown.</param>
	/// <param name="length">How long the countdown runs.</param>
	/// <param name="startsRound">Whether the countdown starts a round when it elapses.</param>
	/// <returns>The countdown that started.</returns>
	/// <remarks>The caller holds the room's scope.</remarks>
	internal RoomCountdown StartCountdown(UserRecord by, TimeSpan length, bool startsRound)
	{
		CancelCountdown(null);

		var countdown = new RoomCountdown(by, length, startsRound, Context.Clock.GetUtcNow());
		Rounds.Countdown = countdown;
		Rounds.CountdownTimers =
		[
			.. RoomCountdown.MarksFor(length, startsRound).Select(mark =>
				Context.Clock.CreateTimer(_ => Fire(() => TickCountdown(countdown, mark)), null, length - mark,
					Timeout.InfiniteTimeSpan)),
			Context.Clock.CreateTimer(_ => Fire(() => ElapseCountdown(countdown)), null, length,
				Timeout.InfiniteTimeSpan)
		];
		Emit(new RoomCountdownStarted(this, countdown));
		return countdown;
	}

	/// <summary>Cancels this room's countdown.</summary>
	/// <param name="announce">
	///     Why the countdown is cancelled when a cancellation event is to be emitted; otherwise
	///     <see langword="null" />.
	/// </param>
	/// <returns><see langword="true" /> when a countdown was cancelled.</returns>
	/// <remarks>The caller holds the room's scope.</remarks>
	internal bool CancelCountdown(RoomCountdownCancelReason? announce)
	{
		var countdown = Rounds.Countdown;
		foreach (var timer in Rounds.CountdownTimers) timer.Dispose();
		Rounds.CountdownTimers = [];
		Rounds.Countdown = null;

		if (countdown is null) return false;
		if (announce is { } reason) Emit(new RoomCountdownCancelled(this, countdown, reason));
		return true;
	}

	private void TickCountdown(RoomCountdown countdown, TimeSpan remaining)
	{
		lock (_sync)
		{
			if (IsClosed || !ReferenceEquals(Rounds.Countdown, countdown)) return;
			Emit(new RoomCountdownTicked(this, countdown, remaining));
		}
	}

	private void ElapseCountdown(RoomCountdown countdown)
	{
		lock (_sync)
		{
			if (IsClosed || !ReferenceEquals(Rounds.Countdown, countdown)) return;

			CancelCountdown(null);
			(Round Round, IReadOnlyList<BanchoConnection> Players)? started = null;
			var result = RoomOutcome.Ok;
			if (countdown.StartsRound)
			{
				if (Rounds.InProgress)
					result = RoomOutcome.InProgress;
				else if (Settings.Beatmap is null)
					result = RoomOutcome.NoBeatmap;
				else
					started = BeginRound();
			}

			Emit(new RoomCountdownElapsed(this, countdown, result));
			if (started is { } round) Emit(new RoomRoundStarted(this, round.Round, round.Players));
		}
	}

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
			// A timer that fails has nothing to report to; the countdown just does not tick.
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
	internal (Round Round, IReadOnlyList<BanchoConnection> Players) BeginRound()
	{
		var round = new Round
		{
			Index = (Rounds.LastRound?.Index ?? 0) + 1,
			Match = Match,
			BeatmapHash = Settings.Beatmap!.Hash,
			Settings = Settings.MatchSettings.Clone(),
			StartedAt = Context.Clock.GetUtcNow(),
			EndedAt = null
		};
		Rounds.LastRound = round;
		Rounds.AllLoadedAnnounced = false;
		Rounds.AllSkippedAnnounced = false;

		var players = new List<BanchoConnection>();
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