using System.Diagnostics.CodeAnalysis;
using Basil.Domain.Multiplayer.Report.Rounds;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Multiplayer.Room;

/// <summary>How the round in progress moved on after a player left or changed.</summary>
/// <param name="Round">The round in progress.</param>
/// <param name="AllLoaded">Whether every playing player has now loaded, announced once per round.</param>
/// <param name="AllSkipped">Whether every playing player has now skipped the intro, announced once per round.</param>
/// <param name="Completed">Whether the round ended because no player was left playing.</param>
public sealed record RoomRoundProgress(Round Round, bool AllLoaded, bool AllSkipped, bool Completed);

/// <summary>What a player's report changed in the round.</summary>
/// <param name="Round">The round the report belongs to; the ended round when the report ended it.</param>
/// <param name="Slot">The index of the reporting player's slot.</param>
/// <param name="All">Whether the report completed the step for every playing player: the last one to load or skip the intro, or the last one to finish, which ends the round.</param>
public readonly record struct RoundSignal(Round Round, int Slot, bool All);

/// <summary>The settlement of a finished round.</summary>
/// <param name="Round">The settled round.</param>
/// <param name="Result">The result of the round, or <see langword="null"/> when no score was received.</param>
/// <param name="Missing">The players whose score never arrived.</param>
public sealed record RoundDecision(Round Round, RoundResult? Result, IReadOnlyList<UserRecord> Missing);

/// <summary>The rounds of a room: starting, running and ending them, and settling the scores of the last one.</summary>
public sealed class RoomRounds
{
	private readonly Room _room;

	internal RoomRounds(Room room)
	{
		_room = room;
	}

	/// <summary>Gets the most recently started round, or <see langword="null"/> when none has started.</summary>
	public Round? LastRound { get; internal set; }

	/// <summary>Gets the round in progress, or <see langword="null"/> when none is running.</summary>
	public Round? CurrentRound => LastRound is { EndedAt: null } round ? round : null;

	/// <summary>Gets whether a round is in progress.</summary>
	public bool InProgress => CurrentRound is not null;

	private bool AllLoadedAnnounced { get; set; }

	private bool AllSkippedAnnounced { get; set; }

	/// <summary>Gets the score settlement of the last round, or <see langword="null"/> when none has started.</summary>
	public RoundSettlement? Settlement { get; internal set; }

	/// <summary>Starts a round on the room's beatmap with every seated player who has it; refused when the room is closed, a round is in progress, or the room has no beatmap.</summary>
	/// <remarks>The round ends at once when no player takes part. Starting also settles the previous round if it was not settled yet.</remarks>
	/// <param name="now">The time the round starts.</param>
	/// <returns>The outcome, the new round, the players taking part, and the settlement of the previous round when this start settled it, otherwise <see langword="null"/>.</returns>
	public (RoomActionResult Outcome, Round? Round, IReadOnlyList<UserRecord> Players, RoundDecision? Previous) StartRound(
		DateTimeOffset now)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, null, [], null);
		if (InProgress) return (RoomActionResult.InProgress, null, [], null);
		if (_room.Settings.Beatmap is null) return (RoomActionResult.NoBeatmap, null, [], null);

		var (round, players, previous) = BeginRound(now);
		return (RoomActionResult.Ok, round, players, previous);
	}

	/// <summary>Ends the round in progress as aborted; refused when the room is closed or no round is in progress.</summary>
	/// <param name="now">The time the round ends.</param>
	/// <returns>The outcome and the aborted round.</returns>
	public (RoomActionResult Outcome, Round? Round) AbortRound(DateTimeOffset now)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, null);
		if (!InProgress) return (RoomActionResult.NotInProgress, null);

		return (RoomActionResult.Ok, EndCurrentRound(true, now));
	}

	/// <summary>Records that a player has loaded the beatmap; refused when the room is closed or the player is not playing in the round in progress.</summary>
	/// <param name="player">The player who loaded.</param>
	/// <returns>The outcome and a signal, which is <see langword="null"/> when the player had already loaded; its <c>All</c> is true only for the report that completes the set.</returns>
	public (RoomActionResult Outcome, RoundSignal? Signal) MarkLoaded(UserRecord player)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, null);
		if (!TryGetPlayingSlot(player, out var round, out var slot)) return (RoomActionResult.NotPlaying, null);
		if (slot.Loaded is true) return (RoomActionResult.Ok, null);

		slot.Loaded = true;
		if (!AllLoadedAnnounced &&
		    _room.Slots.Where(s => s.Status is RoomSlotStatus.Playing).All(s => s.Loaded is true))
		{
			AllLoadedAnnounced = true;
			return (RoomActionResult.Ok, new RoundSignal(round, slot.Index, true));
		}

		return (RoomActionResult.Ok, new RoundSignal(round, slot.Index, false));
	}

	/// <summary>Records that a player skipped the intro; refused when the room is closed or the player is not playing in the round in progress.</summary>
	/// <param name="player">The player who skipped.</param>
	/// <returns>The outcome and a signal, which is <see langword="null"/> when the player had already skipped; its <c>All</c> is true only for the report that completes the set.</returns>
	public (RoomActionResult Outcome, RoundSignal? Signal) SkipIntro(UserRecord player)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, null);
		if (!TryGetPlayingSlot(player, out var round, out var slot)) return (RoomActionResult.NotPlaying, null);
		if (slot.IntroSkipped is true) return (RoomActionResult.Ok, null);

		slot.IntroSkipped = true;
		if (!AllSkippedAnnounced &&
		    _room.Slots.Where(s => s.Status is RoomSlotStatus.Playing).All(s => s.IntroSkipped is true))
		{
			AllSkippedAnnounced = true;
			return (RoomActionResult.Ok, new RoundSignal(round, slot.Index, true));
		}

		return (RoomActionResult.Ok, new RoundSignal(round, slot.Index, false));
	}

	/// <summary>Records that a player failed the beatmap; refused when the room is closed or the player is not playing in the round in progress.</summary>
	/// <param name="player">The player who failed.</param>
	/// <returns>The outcome and a signal for the player.</returns>
	public (RoomActionResult Outcome, RoundSignal? Signal) ReportFailed(UserRecord player)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, null);
		if (!TryGetPlayingSlot(player, out var round, out var slot)) return (RoomActionResult.NotPlaying, null);

		return (RoomActionResult.Ok, new RoundSignal(round, slot.Index, false));
	}

	/// <summary>Records that a player finished the beatmap, ending the round when no player is left playing; refused when the room is closed or the player is not playing in the round in progress.</summary>
	/// <param name="player">The player who finished.</param>
	/// <param name="now">The time the player finished, used as the end of the round when it ends.</param>
	/// <returns>The outcome and a signal whose <c>All</c> is true when this report ended the round.</returns>
	public (RoomActionResult Outcome, RoundSignal? Signal) ReportCompleted(UserRecord player, DateTimeOffset now)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, null);
		if (!TryGetPlayingSlot(player, out var round, out var slot)) return (RoomActionResult.NotPlaying, null);

		slot.SetStatus(RoomSlotStatus.Complete);
		Settlement?.Complete(player);
		if (_room.Slots.Any(s => s.Status is RoomSlotStatus.Playing))
			return (RoomActionResult.Ok, new RoundSignal(round, slot.Index, false));

		var ended = EndCurrentRound(false, now)!;
		return (RoomActionResult.Ok, new RoundSignal(ended, slot.Index, true));
	}

	private bool TryGetPlayingSlot(UserRecord player, [MaybeNullWhen(false)] out Round round,
		[MaybeNullWhen(false)] out RoomSlot slot)
	{
		if (CurrentRound is { } current &&
		    _room.Slots.Find(player) is { Status: RoomSlotStatus.Playing } playing)
		{
			round = current;
			slot = playing;
			return true;
		}

		round = null;
		slot = null;
		return false;
	}

	/// <summary>Ends the round in progress and returns every playing or finished slot to not ready.</summary>
	/// <param name="aborted">Whether the round ends early rather than by being played out; an aborted round also closes its settlement.</param>
	/// <param name="now">The time the round ends.</param>
	/// <returns>The ended round, or <see langword="null"/> when none was in progress.</returns>
	internal Round? EndCurrentRound(bool aborted, DateTimeOffset now)
	{
		if (CurrentRound is not { } round) return null;

		round.EndedAt = now;
		round.Aborted = aborted;
		_room.Slots.ResetPlayerStatus();
		if (aborted) Settlement?.Close();
		return round;
	}

	private (Round Round, IReadOnlyList<UserRecord> Players, RoundDecision? Previous) BeginRound(DateTimeOffset now)
	{
		var round = new Round
		{
			Index = (LastRound?.Index ?? 0) + 1,
			Match = _room.Match,
			BeatmapHash = _room.Settings.Beatmap!.Value.Hash,
			Settings = _room.Settings.Editable.Clone(),
			StartedAt = now,
			EndedAt = null
		};
		LastRound = round;
		AllLoadedAnnounced = false;
		AllSkippedAnnounced = false;

		var players = new List<UserRecord>();
		foreach (var slot in _room.Slots.Where(s => s.Player is not null && s.Status is not RoomSlotStatus.NoMap))
		{
			slot.SetStatus(RoomSlotStatus.Playing);
			players.Add(slot.Player!);
		}

		var previous = Settle().Decision;
		Settlement?.Close();
		Settlement = new RoundSettlement(round, players.Select(player => (player, _room.Slots.Find(player)?.Team)));
		if (players.Count == 0) EndCurrentRound(false, now);
		return (round, players, previous);
	}

	/// <summary>Moves the round in progress on after a seat changed: ends it when nobody is left playing, and reports a first all-loaded or all-skipped state.</summary>
	/// <param name="now">The time of the change.</param>
	/// <returns>What changed, or <see langword="null"/> when no round is in progress or nothing new happened.</returns>
	internal RoomRoundProgress? Advance(DateTimeOffset now)
	{
		if (CurrentRound is not { } round) return null;

		var playing = _room.Slots.Where(s => s.Status is RoomSlotStatus.Playing).ToList();
		if (playing.Count == 0)
		{
			EndCurrentRound(false, now);
			return new RoomRoundProgress(round, false, false, true);
		}

		var allLoaded = !AllLoadedAnnounced && playing.All(s => s.Loaded is true);
		if (allLoaded) AllLoadedAnnounced = true;

		var allSkipped = !AllSkippedAnnounced && playing.All(s => s.IntroSkipped is true);
		if (allSkipped) AllSkippedAnnounced = true;

		return allLoaded || allSkipped ? new RoomRoundProgress(round, allLoaded, allSkipped, false) : null;
	}

	/// <summary>Accepts a player's score for the last round; refused when the room is closed or the settlement does not await a score from that player for that beatmap.</summary>
	/// <param name="score">The submitted score.</param>
	/// <param name="player">The player who submitted it.</param>
	/// <returns>The outcome, the settlement the score joined, and the recorded round score.</returns>
	public (RoomActionResult Outcome, RoundSettlement? Settlement, RoundScore? Score) ReceiveScore(ScoreRecord score,
		UserRecord player)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, null, null);
		if (Settlement is not { } settlement || !settlement.Awaits(player, out var team) ||
		    settlement.Round.BeatmapHash != score.Value.BeatmapHash)
			return (RoomActionResult.RoundMismatch, null, null);

		var roundScore = new RoundScore(score, settlement.Round, team);
		settlement.AddScore(roundScore);
		return (RoomActionResult.Ok, settlement, roundScore);
	}

	/// <summary>Gets whether the last round's settlement still awaits a score from a player for a beatmap.</summary>
	/// <param name="player">The player.</param>
	/// <param name="beatmap">The hash of the beatmap the score would be for.</param>
	public bool AwaitsScore(UserRecord player, Md5Hash beatmap)
	{
		return !_room.IsClosed && Settlement is { } settlement && settlement.Round.BeatmapHash == beatmap &&
		       settlement.Awaits(player, out _);
	}

	/// <summary>Settles the last round: closes its settlement and decides its result; refused when there is no settlement, its round is still running or it is already closed.</summary>
	/// <returns>The outcome and the decision, which is <see langword="null"/> when no player took part.</returns>
	public (RoomActionResult Outcome, RoundDecision? Decision) Settle()
	{
		if (Settlement is not { } settlement) return (RoomActionResult.NotInProgress, null);
		if (settlement.Round.EndedAt is null) return (RoomActionResult.InProgress, null);
		if (settlement.Closed) return (RoomActionResult.RoundMismatch, null);

		return (RoomActionResult.Ok, Decide(settlement));
	}

	private static RoundDecision? Decide(RoundSettlement settlement)
	{
		var missing = settlement.Missing.ToList();
		settlement.Close();
		if (!settlement.HasParticipants) return null;

		var result = RoundResult.Decide(settlement.Round, settlement.Scores);
		settlement.Round.Result = result;
		return new RoundDecision(settlement.Round, result, missing);
	}
}