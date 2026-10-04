using System.Diagnostics.CodeAnalysis;
using Basil.Application.Contracts.Multiplayer;
using Basil.Application.Contracts.Multiplayer.Events;
using Basil.Application.Storage.Multiplayer;
using Basil.Application.Storage.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Services.Multiplayer;

internal sealed partial class RoomService
{
	/// <summary>Gets the remaining times at which a countdown announces itself, longest first.</summary>
	/// <param name="length">The countdown's length.</param>
	/// <param name="startsRound">Whether the countdown starts the round when it ends.</param>
	/// <returns>
	///     Every mark is strictly below <paramref name="length" />. A countdown that starts the round announces at 60, 30, 10,
	///     5,
	///     4 and 3 seconds and at every whole minute (120, 180, ...), skipping any whole-minute mark, 60 included, within 5
	///     seconds of the length. Any other countdown announces at 60, 30, 10 and 5 seconds.
	/// </returns>
	internal static IReadOnlyList<TimeSpan> MarksFor(TimeSpan length, bool startsRound)
	{
		int[] seconds = startsRound ? [30, 10, 5, 4, 3] : [60, 30, 10, 5];
		var marks = seconds.Select(second => TimeSpan.FromSeconds(second)).Where(mark => mark < length).ToList();

		if (startsRound)
			for (var minute = TimeSpan.FromMinutes(1); minute < length; minute += TimeSpan.FromMinutes(1))
				if (length - minute > TimeSpan.FromSeconds(5))
					marks.Add(minute);

		return [.. marks.OrderByDescending(mark => mark)];
	}

	private RoomResult Start(Room room, Connection by)
	{
		if (!RoomRules.IsHostOrManager(room, by)) return RoomResult.NotAuthorized;
		if (room.InProgress) return RoomResult.InProgress;
		if (room.Beatmap is null) return RoomResult.NoBeatmap;

		RoundMechanics.StopCountdown(room);
		StartRound(room, false);
		return RoomResult.Ok;
	}

	private RoomResult Abort(Room room, Connection by)
	{
		if (!RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;
		if (!room.InProgress) return RoomResult.NotInProgress;

		RoundMechanics.StopCountdown(room);
		AbortRound(room);
		return RoomResult.Ok;
	}

	private RoomResult MarkLoaded(Room room, BanchoConnection by)
	{
		if (!TryGetPlayingSlot(room, by, out var round, out var slot)) return RoomResult.NotPlaying;
		if (slot.Loaded is true) return RoomResult.Ok;

		slot.Loaded = true;
		if (!room.AllLoadedAnnounced &&
		    room.Slots.Where(s => s.Status is RoomSlotStatus.Playing).All(s => s.Loaded is true))
		{
			room.AllLoadedAnnounced = true;
			Emit(new RoomRoundAllLoaded(room, round, slot.Index));
		}
		else
		{
			Emit(new RoomRoundPlayerLoaded(room, round, slot.Index));
		}

		return RoomResult.Ok;
	}

	private RoomResult Skip(Room room, BanchoConnection by)
	{
		if (!TryGetPlayingSlot(room, by, out var round, out var slot)) return RoomResult.NotPlaying;
		if (slot.IntroSkipped is true) return RoomResult.Ok;

		slot.IntroSkipped = true;
		if (!room.AllSkippedAnnounced &&
		    room.Slots.Where(s => s.Status is RoomSlotStatus.Playing).All(s => s.IntroSkipped is true))
		{
			room.AllSkippedAnnounced = true;
			Emit(new RoomRoundAllSkipped(room, round, slot.Index));
		}
		else
		{
			Emit(new RoomRoundPlayerSkipped(room, round, slot.Index));
		}

		return RoomResult.Ok;
	}

	private RoomResult Fail(Room room, BanchoConnection by)
	{
		if (!TryGetPlayingSlot(room, by, out var round, out var slot)) return RoomResult.NotPlaying;

		Emit(new RoomRoundPlayerFailed(room, round, slot.Index));
		return RoomResult.Ok;
	}

	private RoomResult Complete(Room room, BanchoConnection by)
	{
		if (!TryGetPlayingSlot(room, by, out var round, out var slot)) return RoomResult.NotPlaying;

		RoomSlotsMechanics.SetStatus(slot, RoomSlotStatus.Complete);
		if (room.Slots.Any(s => s.Status is RoomSlotStatus.Playing))
			Emit(new RoomRoundPlayerCompleted(room, round, slot.Index));
		else
			EndRound(room, slot.Index);
		return RoomResult.Ok;
	}

	private RoomResult StartCountdown(Room room, Connection by, TimeSpan length, bool startsRound)
	{
		if (!RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;
		if (length <= TimeSpan.Zero || length > MaxCountdownLength) return RoomResult.OutOfRange;
		switch (startsRound)
		{
			case true when room.InProgress:
				return RoomResult.InProgress;
			case true when room.Beatmap is null:
				return RoomResult.NoBeatmap;
		}

		RoundMechanics.StopCountdown(room);
		Countdown? countdown = null;
		var milestones = MarksFor(length, startsRound)
			.Select(mark => new Countdown.Milestone(mark, _ => TickAsync(room, countdown!, mark)))
			.Append(new Countdown.Milestone(TimeSpan.Zero, _ => ElapseAsync(room, countdown!, startsRound)));
		countdown = new Countdown(length, milestones, time);
		room.CountdownTimer = countdown;
		room.CountdownStartsRound = startsRound;
		countdown.Start();
		room.CountdownEndsAt = countdown.EndsAt;
		Emit(new RoomCountdownStarted(room, length, startsRound, countdown.EndsAt!.Value));
		return RoomResult.Ok;
	}

	private RoomResult CancelCountdown(Room room, Connection by)
	{
		if (!RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;
		if (room.CountdownTimer is null) return RoomResult.NoCountdown;

		RoundMechanics.StopCountdown(room);
		Emit(new RoomCountdownCancelled(room));
		return RoomResult.Ok;
	}

	private RoomResult RecordScore(Room room, User player, Score score)
	{
		if (room.LastRound is not { } round || !round.Equals(score.Value.Round)) return RoomResult.RoundMismatch;

		Emit(new RoomRoundScoreSubmitted(room, round, player, score));
		return RoomResult.Ok;
	}

	/// <summary>Finds the round in progress and the slot of a connection that is playing it.</summary>
	/// <param name="room">The room to look in.</param>
	/// <param name="by">The connection to look for.</param>
	/// <param name="round">The round in progress, when the connection is playing it.</param>
	/// <param name="slot">The connection's slot, when it is playing.</param>
	/// <returns>
	///     <see langword="true" /> if a round is in progress and the connection is playing it; otherwise,
	///     <see langword="false" />.
	/// </returns>
	private static bool TryGetPlayingSlot(Room room, BanchoConnection by, [MaybeNullWhen(false)] out Round round,
		[MaybeNullWhen(false)] out RoomSlot slot)
	{
		if (room.CurrentRound is { } current && room.Slots.Find(by) is { Status: RoomSlotStatus.Playing } playing)
		{
			round = current;
			slot = playing;
			return true;
		}

		round = null;
		slot = null;
		return false;
	}

	/// <summary>Works out what the players who are still playing have all done, after a player stopped playing.</summary>
	/// <param name="room">The room whose round to look at.</param>
	/// <returns>
	///     What changed for the round in progress, or <see langword="null" /> when no round is in progress or nothing
	///     changed.
	/// </returns>
	/// <remarks>
	///     Ends the round when nobody is playing any more; otherwise reports that every remaining player has loaded or
	///     skipped, once per round.
	/// </remarks>
	private RoomRoundProgress? AdvanceRound(Room room)
	{
		if (room.CurrentRound is not { } round) return null;

		var playing = room.Slots.Where(s => s.Status is RoomSlotStatus.Playing).ToList();
		if (playing.Count == 0)
		{
			RoundMechanics.EndCurrentRound(room, time.GetUtcNow(), false);
			return new RoomRoundProgress(round, false, false, true);
		}

		var allLoaded = !room.AllLoadedAnnounced && playing.All(s => s.Loaded is true);
		if (allLoaded) room.AllLoadedAnnounced = true;

		var allSkipped = !room.AllSkippedAnnounced && playing.All(s => s.IntroSkipped is true);
		if (allSkipped) room.AllSkippedAnnounced = true;

		return allLoaded || allSkipped ? new RoomRoundProgress(round, allLoaded, allSkipped, false) : null;
	}

	private Round StartRound(Room room, bool byCountdown)
	{
		var round = new Round
		{
			Number = (room.LastRound?.Number ?? 0) + 1,
			Match = room.Match,
			BeatmapHash = room.Beatmap!.Hash,
			Settings = room.Settings.Clone(),
			StartedAt = time.GetUtcNow(),
			EndedAt = null
		};
		room.LastRound = round;
		room.AllLoadedAnnounced = false;
		room.AllSkippedAnnounced = false;

		var players = new List<BanchoConnection>();
		foreach (var slot in room.Slots.Where(s => s.Player is not null && s.Status is not RoomSlotStatus.NoMap))
		{
			RoomSlotsMechanics.SetStatus(slot, RoomSlotStatus.Playing);
			players.Add(slot.Player!);
		}

		if (players.Count == 0) RoundMechanics.EndCurrentRound(room, time.GetUtcNow(), false);
		Emit(new RoomRoundStarted(room, round, players, byCountdown));
		return round;
	}

	private void AbortRound(Room room)
	{
		var round = RoundMechanics.EndCurrentRound(room, time.GetUtcNow(), true)!;
		Emit(new RoomRoundAborted(room, round));
	}

	private void EndRound(Room room, int slot)
	{
		var round = RoundMechanics.EndCurrentRound(room, time.GetUtcNow(), false)!;
		Emit(new RoomRoundCompleted(room, round, slot));
	}

	private async Task TickAsync(Room room, Countdown countdown, TimeSpan mark)
	{
		await using var scope = await Lobby.EnterAsync(room);
		if (scope is null || !ReferenceEquals(room.CountdownTimer, countdown)) return;

		Emit(new RoomCountdownTicked(room, mark));
	}

	private async Task ElapseAsync(Room room, Countdown countdown, bool startsRound)
	{
		await using var scope = await Lobby.EnterAsync(room);
		if (scope is null || !ReferenceEquals(room.CountdownTimer, countdown)) return;

		RoundMechanics.StopCountdown(room);
		if (startsRound && room is { InProgress: false, Beatmap: not null })
			StartRound(room, true);
		else
			Emit(new RoomCountdownElapsed(room, startsRound));
	}
}