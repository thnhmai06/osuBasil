using System.Diagnostics.CodeAnalysis;
using Basil.Domain.Multiplayer.Report.Rounds;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Multiplayer.Room;

public sealed record RoomRoundProgress(Round Round, bool AllLoaded, bool AllSkipped, bool Completed);

public readonly record struct RoundSignal(Round Round, int Slot, bool All);

public sealed record RoundDecision(Round Round, RoundResult? Result, IReadOnlyList<UserRecord> Missing);

public sealed class RoomRounds
{
	private readonly Room _room;

	internal RoomRounds(Room room)
	{
		_room = room;
	}

	public Round? LastRound { get; internal set; }

	public Round? CurrentRound => LastRound is { EndedAt: null } round ? round : null;

	public bool InProgress => CurrentRound is not null;

	private bool AllLoadedAnnounced { get; set; }

	private bool AllSkippedAnnounced { get; set; }

	public RoundSettlement? Settlement { get; internal set; }

	public (RoomActionResult Outcome, Round? Round, IReadOnlyList<UserRecord> Players, RoundDecision? Previous) StartRound(
		DateTimeOffset now)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, null, [], null);
		if (InProgress) return (RoomActionResult.InProgress, null, [], null);
		if (_room.Settings.Beatmap is null) return (RoomActionResult.NoBeatmap, null, [], null);

		var (round, players, previous) = BeginRound(now);
		return (RoomActionResult.Ok, round, players, previous);
	}

	public (RoomActionResult Outcome, Round? Round) AbortRound(DateTimeOffset now)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, null);
		if (!InProgress) return (RoomActionResult.NotInProgress, null);

		return (RoomActionResult.Ok, EndCurrentRound(true, now));
	}

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

	public (RoomActionResult Outcome, RoundSignal? Signal) ReportFailed(UserRecord player)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, null);
		if (!TryGetPlayingSlot(player, out var round, out var slot)) return (RoomActionResult.NotPlaying, null);

		return (RoomActionResult.Ok, new RoundSignal(round, slot.Index, false));
	}

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

		var previous = DecideForClosing();
		Settlement?.Close();
		Settlement = new RoundSettlement(round, players.Select(player => (player, _room.Slots.Find(player)?.Team)));
		if (players.Count == 0) EndCurrentRound(false, now);
		return (round, players, previous);
	}

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

	public (RoomActionResult Outcome, RoundSettlement? Settlement, RoundScore? Score) ReceiveScore(ScoreRecord score,
		UserRecord player)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, null, null);
		if (Settlement is not { } settlement || !settlement.Awaits(player, out var team) ||
		    settlement.Round.BeatmapHash != score.Value.BeatmapHash)
			return (RoomActionResult.RoundMismatch, null, null);

		var roundScore = new RoundScore(score, settlement.Round, team);
		settlement.Scores.Add(roundScore);
		return (RoomActionResult.Ok, settlement, roundScore);
	}

	public bool AwaitsScore(UserRecord player, Md5Hash beatmap)
	{
		return !_room.IsClosed && Settlement is { } settlement && settlement.Round.BeatmapHash == beatmap &&
		       settlement.Awaits(player, out _);
	}

	public RoundDecision? DecideForClosing()
	{
		return Settlement is { Closed: false, Round.EndedAt: not null } settlement ? Decide(settlement) : null;
	}

	public static RoundDecision? Decide(RoundSettlement settlement)
	{
		var missing = settlement.Missing();
		settlement.Close();
		if (!settlement.HasParticipants) return null;

		var result = RoundResult.Decide(settlement.Round, settlement.Scores);
		settlement.Round.Result = result;
		return new RoundDecision(settlement.Round, result, missing);
	}
}