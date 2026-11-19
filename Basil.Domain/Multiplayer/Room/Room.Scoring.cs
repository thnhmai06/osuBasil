using Basil.Domain.Scores;
using Basil.Domain.Multiplayer.Report.Rounds;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Multiplayer.Room;

public sealed partial class Room
{
	public (RoomOutcome Outcome, RoundSettlement? Settlement, RoundScore? Score) ReceiveScore(ScoreRecord score,
		UserRecord player)
	{
		if (IsClosed) return (RoomOutcome.RoomClosed, null, null);
		if (Rounds.Settlement is not { } settlement || !settlement.Awaits(player, out var team) ||
		    settlement.Round.BeatmapHash != score.Value.BeatmapHash)
			return (RoomOutcome.RoundMismatch, null, null);

		var roundScore = new RoundScore(score, settlement.Round, team);
		settlement.Scores.Add(roundScore);
		return (RoomOutcome.Ok, settlement, roundScore);
	}

	public bool AwaitsScore(UserRecord player, Md5Hash beatmap)
	{
		return !IsClosed && Rounds.Settlement is { } settlement && settlement.Round.BeatmapHash == beatmap &&
		       settlement.Awaits(player, out _);
	}

	public RoundDecision? DecideForClosing()
	{
		return Rounds.Settlement is { Closed: false, Round.EndedAt: not null } settlement ? Decide(settlement) : null;
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