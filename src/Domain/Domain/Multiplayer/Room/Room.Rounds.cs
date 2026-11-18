using System.Diagnostics.CodeAnalysis;
using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer;

public sealed record RoomRoundProgress(Round Round, bool AllLoaded, bool AllSkipped, bool Completed);

public readonly record struct RoundSignal(Round Round, int Slot, bool All);

public sealed record RoundDecision(Round Round, RoundResult? Result, IReadOnlyList<UserRecord> Missing);

public sealed partial class Room
{
	public (RoomOutcome Outcome, Round? Round, IReadOnlyList<UserRecord> Players, RoundDecision? Previous) StartRound(
		DateTimeOffset now)
	{
		if (IsClosed) return (RoomOutcome.RoomClosed, null, [], null);
		if (Rounds.InProgress) return (RoomOutcome.InProgress, null, [], null);
		if (Settings.Beatmap is null) return (RoomOutcome.NoBeatmap, null, [], null);

		var (round, players, previous) = BeginRound(now);
		return (RoomOutcome.Ok, round, players, previous);
	}

	public (RoomOutcome Outcome, Round? Round) AbortRound(DateTimeOffset now)
	{
		if (IsClosed) return (RoomOutcome.RoomClosed, null);
		if (!Rounds.InProgress) return (RoomOutcome.NotInProgress, null);

		return (RoomOutcome.Ok, EndCurrentRound(true, now));
	}

	public (RoomOutcome Outcome, RoundSignal? Signal) MarkLoaded(UserRecord player)
	{
		if (IsClosed) return (RoomOutcome.RoomClosed, null);
		if (!TryGetPlayingSlot(player, out var round, out var slot)) return (RoomOutcome.NotPlaying, null);
		if (slot.Loaded is true) return (RoomOutcome.Ok, null);

		slot.Loaded = true;
		if (!Rounds.AllLoadedAnnounced &&
		    Slots.Where(s => s.Status is RoomSlotStatus.Playing).All(s => s.Loaded is true))
		{
			Rounds.AllLoadedAnnounced = true;
			return (RoomOutcome.Ok, new RoundSignal(round, slot.Index, true));
		}

		return (RoomOutcome.Ok, new RoundSignal(round, slot.Index, false));
	}

	public (RoomOutcome Outcome, RoundSignal? Signal) SkipIntro(UserRecord player)
	{
		if (IsClosed) return (RoomOutcome.RoomClosed, null);
		if (!TryGetPlayingSlot(player, out var round, out var slot)) return (RoomOutcome.NotPlaying, null);
		if (slot.IntroSkipped is true) return (RoomOutcome.Ok, null);

		slot.IntroSkipped = true;
		if (!Rounds.AllSkippedAnnounced &&
		    Slots.Where(s => s.Status is RoomSlotStatus.Playing).All(s => s.IntroSkipped is true))
		{
			Rounds.AllSkippedAnnounced = true;
			return (RoomOutcome.Ok, new RoundSignal(round, slot.Index, true));
		}

		return (RoomOutcome.Ok, new RoundSignal(round, slot.Index, false));
	}

	public (RoomOutcome Outcome, RoundSignal? Signal) ReportFailed(UserRecord player)
	{
		if (IsClosed) return (RoomOutcome.RoomClosed, null);
		if (!TryGetPlayingSlot(player, out var round, out var slot)) return (RoomOutcome.NotPlaying, null);

		return (RoomOutcome.Ok, new RoundSignal(round, slot.Index, false));
	}

	public (RoomOutcome Outcome, RoundSignal? Signal) ReportCompleted(UserRecord player, DateTimeOffset now)
	{
		if (IsClosed) return (RoomOutcome.RoomClosed, null);
		if (!TryGetPlayingSlot(player, out var round, out var slot)) return (RoomOutcome.NotPlaying, null);

		slot.SetStatus(RoomSlotStatus.Complete);
		Rounds.Settlement?.Complete(player);
		if (Slots.Any(s => s.Status is RoomSlotStatus.Playing))
			return (RoomOutcome.Ok, new RoundSignal(round, slot.Index, false));

		var ended = EndCurrentRound(false, now)!;
		return (RoomOutcome.Ok, new RoundSignal(ended, slot.Index, true));
	}

	private bool TryGetPlayingSlot(UserRecord player, [MaybeNullWhen(false)] out Round round,
		[MaybeNullWhen(false)] out RoomSlot slot)
	{
		if (Rounds.CurrentRound is { } current &&
		    Slots.Find(player) is { Status: RoomSlotStatus.Playing } playing)
		{
			round = current;
			slot = playing;
			return true;
		}

		round = null;
		slot = null;
		return false;
	}

	public Round? EndCurrentRound(bool aborted, DateTimeOffset now)
	{
		if (Rounds.CurrentRound is not { } round) return null;

		round.EndedAt = now;
		round.Aborted = aborted;
		Slots.ResetPlayers();
		if (aborted)
			Rounds.Settlement?.Close();
		else
			Rounds.SettlePending = true;
		return round;
	}

	private (Round Round, IReadOnlyList<UserRecord> Players, RoundDecision? Previous) BeginRound(DateTimeOffset now)
	{
		var round = new Round
		{
			Index = (Rounds.LastRound?.Index ?? 0) + 1,
			Match = Match,
			BeatmapHash = Settings.Beatmap!.Hash,
			Settings = Settings.Editable.Clone(),
			StartedAt = now,
			EndedAt = null
		};
		Rounds.LastRound = round;
		Rounds.AllLoadedAnnounced = false;
		Rounds.AllSkippedAnnounced = false;

		var players = new List<UserRecord>();
		foreach (var slot in Slots.Where(s => s.Player is not null && s.Status is not RoomSlotStatus.NoMap))
		{
			slot.SetStatus(RoomSlotStatus.Playing);
			players.Add(slot.Player!);
		}

		var previous = DecideForClosing();
		Rounds.Settlement?.Close();
		Rounds.Settlement = new RoundSettlement(round, players.Select(player => (player, Slots.Find(player)?.Team)));
		if (players.Count == 0) EndCurrentRound(false, now);
		return (round, players, previous);
	}

	public RoomRoundProgress? AdvanceRound(DateTimeOffset now)
	{
		if (Rounds.CurrentRound is not { } round) return null;

		var playing = Slots.Where(s => s.Status is RoomSlotStatus.Playing).ToList();
		if (playing.Count == 0)
		{
			EndCurrentRound(false, now);
			return new RoomRoundProgress(round, false, false, true);
		}

		var allLoaded = !Rounds.AllLoadedAnnounced && playing.All(s => s.Loaded is true);
		if (allLoaded) Rounds.AllLoadedAnnounced = true;

		var allSkipped = !Rounds.AllSkippedAnnounced && playing.All(s => s.IntroSkipped is true);
		if (allSkipped) Rounds.AllSkippedAnnounced = true;

		return allLoaded || allSkipped ? new RoomRoundProgress(round, allLoaded, allSkipped, false) : null;
	}
}
