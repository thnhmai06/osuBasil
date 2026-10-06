using Basil.Application.Contracts.Multiplayer;
using Basil.Application.Contracts.Multiplayer.Events;
using Basil.Application.Contracts.Multiplayer.Rooms;
using Basil.Application.Storage.Multiplayer;
using Basil.Application.Storage.Sessions;
using System.Diagnostics.CodeAnalysis;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Services.Multiplayer.Rooms;

/// <summary>Runs rounds and countdowns of rooms and records the scores of their rounds.</summary>
internal sealed class RoomRoundsService(RoomEventStream events, TimeProvider time) : IRoomRoundsService
{
	/// <summary>The countdown length used when none is given.</summary>
	internal static readonly TimeSpan DefaultCountdownLength = TimeSpan.FromSeconds(30);

	/// <summary>The longest countdown allowed.</summary>
	internal static readonly TimeSpan MaxCountdownLength = TimeSpan.FromHours(1);

	/// <inheritdoc />
	public Task<RoomResult> StartAsync(Room room, Connection by, CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => Start(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> AbortAsync(Room room, Connection by, CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => Abort(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> MarkLoadedAsync(Room room, BanchoConnection by,
		CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => MarkLoaded(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> SkipAsync(Room room, BanchoConnection by, CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => Skip(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> FailAsync(Room room, BanchoConnection by, CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => Fail(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> CompleteAsync(Room room, BanchoConnection by,
		CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => Complete(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> StartCountdownAsync(Room room, Connection by, TimeSpan length, bool startsRound,
		CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => StartCountdown(room, by, length, startsRound), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> CancelCountdownAsync(Room room, Connection by,
		CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => CancelCountdown(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> RecordScoreAsync(Room room, User player, Score score,
		CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => RecordScore(room, player, score), cancellationToken);
	}

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
		if (!RoomRules.IsHostOrManager(room, by, time.GetUtcNow())) return RoomResult.NotAuthorized;
		if (room.Rounds.InProgress) return RoomResult.InProgress;
		if (room.Settings.Beatmap is null) return RoomResult.NoBeatmap;

		RoundMechanics.StopCountdown(room);
		StartRound(room, false);
		return RoomResult.Ok;
	}

	private RoomResult Abort(Room room, Connection by)
	{
		if (!RoomRules.CanManage(room, by, time.GetUtcNow())) return RoomResult.NotAuthorized;
		if (!room.Rounds.InProgress) return RoomResult.NotInProgress;

		RoundMechanics.StopCountdown(room);
		AbortRound(room);
		return RoomResult.Ok;
	}

	private RoomResult MarkLoaded(Room room, BanchoConnection by)
	{
		if (!TryGetPlayingSlot(room, by, out var round, out var slot)) return RoomResult.NotPlaying;
		if (slot.Loaded is true) return RoomResult.Ok;

		slot.Loaded = true;
		if (!room.Rounds.AllLoadedAnnounced &&
		    room.Slots.Where(s => s.Status is RoomSlotStatus.Playing).All(s => s.Loaded is true))
		{
			room.Rounds.AllLoadedAnnounced = true;
			events.Emit(new RoomRoundAllLoaded(room, round, slot.Index));
		}
		else
		{
			events.Emit(new RoomRoundPlayerLoaded(room, round, slot.Index));
		}

		return RoomResult.Ok;
	}

	private RoomResult Skip(Room room, BanchoConnection by)
	{
		if (!TryGetPlayingSlot(room, by, out var round, out var slot)) return RoomResult.NotPlaying;
		if (slot.IntroSkipped is true) return RoomResult.Ok;

		slot.IntroSkipped = true;
		if (!room.Rounds.AllSkippedAnnounced &&
		    room.Slots.Where(s => s.Status is RoomSlotStatus.Playing).All(s => s.IntroSkipped is true))
		{
			room.Rounds.AllSkippedAnnounced = true;
			events.Emit(new RoomRoundAllSkipped(room, round, slot.Index));
		}
		else
		{
			events.Emit(new RoomRoundPlayerSkipped(room, round, slot.Index));
		}

		return RoomResult.Ok;
	}

	private RoomResult Fail(Room room, BanchoConnection by)
	{
		if (!TryGetPlayingSlot(room, by, out var round, out var slot)) return RoomResult.NotPlaying;

		events.Emit(new RoomRoundPlayerFailed(room, round, slot.Index));
		return RoomResult.Ok;
	}

	private RoomResult Complete(Room room, BanchoConnection by)
	{
		if (!TryGetPlayingSlot(room, by, out var round, out var slot)) return RoomResult.NotPlaying;

		RoomSlotsMechanics.SetStatus(slot, RoomSlotStatus.Complete);
		if (room.Slots.Any(s => s.Status is RoomSlotStatus.Playing))
			events.Emit(new RoomRoundPlayerCompleted(room, round, slot.Index));
		else
			EndRound(room, slot.Index);
		return RoomResult.Ok;
	}

	private RoomResult StartCountdown(Room room, Connection by, TimeSpan length, bool startsRound)
	{
		if (!RoomRules.CanManage(room, by, time.GetUtcNow())) return RoomResult.NotAuthorized;
		if (length <= TimeSpan.Zero || length > MaxCountdownLength) return RoomResult.OutOfRange;
		switch (startsRound)
		{
			case true when room.Rounds.InProgress:
				return RoomResult.InProgress;
			case true when room.Settings.Beatmap is null:
				return RoomResult.NoBeatmap;
		}

		RoundMechanics.StopCountdown(room);
		var milestones = MarksFor(length, startsRound)
			.Select(mark => new Countdown.Milestone(mark, countdown => TickAsync(room, countdown, mark)))
			.Append(new Countdown.Milestone(TimeSpan.Zero, countdown => ElapseAsync(room, countdown, startsRound)));
		var countdown = new Countdown(length, milestones, time);
		room.Rounds.CountdownTimer = countdown;
		room.Rounds.CountdownStartsRound = startsRound;
		room.Rounds.CountdownEndsAt = countdown.EndsAt;
		events.Emit(new RoomCountdownStarted(room, length, startsRound, countdown.EndsAt));
		return RoomResult.Ok;
	}

	private RoomResult CancelCountdown(Room room, Connection by)
	{
		if (!RoomRules.CanManage(room, by, time.GetUtcNow())) return RoomResult.NotAuthorized;
		if (room.Rounds.CountdownTimer is null) return RoomResult.NoCountdown;

		RoundMechanics.StopCountdown(room);
		events.Emit(new RoomCountdownCancelled(room));
		return RoomResult.Ok;
	}

	private RoomResult RecordScore(Room room, User player, Score score)
	{
		if (room.Rounds.LastRound is not { } round || !round.Equals(score.Value.Round)) return RoomResult.RoundMismatch;

		events.Emit(new RoomRoundScoreSubmitted(room, round, player, score));
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
		if (room.Rounds.CurrentRound is { } current && room.Slots.Find(by) is { Status: RoomSlotStatus.Playing } playing)
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
	internal RoomRoundProgress? AdvanceRound(Room room)
	{
		if (room.Rounds.CurrentRound is not { } round) return null;

		var playing = room.Slots.Where(s => s.Status is RoomSlotStatus.Playing).ToList();
		if (playing.Count == 0)
		{
			RoundMechanics.EndCurrentRound(room, time.GetUtcNow(), false);
			return new RoomRoundProgress(round, false, false, true);
		}

		var allLoaded = !room.Rounds.AllLoadedAnnounced && playing.All(s => s.Loaded is true);
		if (allLoaded) room.Rounds.AllLoadedAnnounced = true;

		var allSkipped = !room.Rounds.AllSkippedAnnounced && playing.All(s => s.IntroSkipped is true);
		if (allSkipped) room.Rounds.AllSkippedAnnounced = true;

		return allLoaded || allSkipped ? new RoomRoundProgress(round, allLoaded, allSkipped, false) : null;
	}

	private Round StartRound(Room room, bool byCountdown)
	{
		var round = new Round
		{
			Number = (room.Rounds.LastRound?.Number ?? 0) + 1,
			Match = room.Match,
			BeatmapHash = room.Settings.Beatmap!.Hash,
			Settings = room.Settings.Stored.Clone(),
			StartedAt = time.GetUtcNow(),
			EndedAt = null
		};
		room.Rounds.LastRound = round;
		room.Rounds.AllLoadedAnnounced = false;
		room.Rounds.AllSkippedAnnounced = false;

		var players = new List<BanchoConnection>();
		foreach (var slot in room.Slots.Where(s => s.Player is not null && s.Status is not RoomSlotStatus.NoMap))
		{
			RoomSlotsMechanics.SetStatus(slot, RoomSlotStatus.Playing);
			players.Add(slot.Player!);
		}

		if (players.Count == 0) RoundMechanics.EndCurrentRound(room, time.GetUtcNow(), false);
		events.Emit(new RoomRoundStarted(room, round, players, byCountdown));
		return round;
	}

	private void AbortRound(Room room)
	{
		var round = RoundMechanics.EndCurrentRound(room, time.GetUtcNow(), true)!;
		events.Emit(new RoomRoundAborted(room, round));
	}

	private void EndRound(Room room, int slot)
	{
		var round = RoundMechanics.EndCurrentRound(room, time.GetUtcNow(), false)!;
		events.Emit(new RoomRoundCompleted(room, round, slot));
	}

	private async Task TickAsync(Room room, Countdown countdown, TimeSpan mark)
	{
		await using var scope = await room.EnterAsync();
		if (scope is null || !ReferenceEquals(room.Rounds.CountdownTimer, countdown)) return;

		events.Emit(new RoomCountdownTicked(room, mark));
	}

	private async Task ElapseAsync(Room room, Countdown countdown, bool startsRound)
	{
		await using var scope = await room.EnterAsync();
		if (scope is null || !ReferenceEquals(room.Rounds.CountdownTimer, countdown)) return;

		RoundMechanics.StopCountdown(room);
		if (startsRound && room is { Rounds.InProgress: false, Settings.Beatmap: not null })
			StartRound(room, true);
		else
			events.Emit(new RoomCountdownElapsed(room, startsRound));
	}
}
