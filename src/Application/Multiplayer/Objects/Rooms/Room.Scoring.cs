using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Users.Objects.Connections;

using Basil.Domain.Multiplayer;
using Basil.Domain.Game.Scores;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed partial class Room
{
	internal RoomOutcome ReceiveScore(ScoreRecord score, UserRecord player)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (Rounds.Settlement is not { } settlement || !settlement.Awaits(player, out var team) ||
			    settlement.Round.BeatmapHash != score.Value.BeatmapHash)
				return RoomOutcome.RoundMismatch;

			var roundScore = new RoundScore(score, settlement.Round, team);
			settlement.Scores.Add(roundScore);
			Context.Events.Publish(new RoomRoundScoreSubmitted(this, settlement.Round, player, roundScore));
			if (settlement.Round.EndedAt is not null && settlement.Missing().Count == 0) Decide(settlement);
			return RoomOutcome.Ok;
		}
	}

	internal bool AwaitsScore(UserRecord player, Md5Hash beatmap)
	{
		lock (_sync)
		{
			return !IsClosed && Rounds.Settlement is { } settlement && settlement.Round.BeatmapHash == beatmap &&
			       settlement.Awaits(player, out _);
		}
	}

	private void BeginSettlement(Round round, IReadOnlyList<StableConnection> players)
	{
		DecideForClosing();
		Rounds.Settlement?.Close();
		Rounds.Settlement = new RoundSettlement(round,
			players.Select(player => (player.Record, Slots.Find(player)?.Team)));
	}

	private void SettleRound()
	{
		if (Rounds.Settlement is not { Closed: false } settlement || settlement.Round.EndedAt is null) return;

		var wait = Context.Settings.Current.Multiplayer.Rounds.ScoreWait;
		if (settlement.HasCompletions)
			Context.Events.Publish(new RoomRoundEnded(this, settlement.Round, wait));

		if (settlement.Missing().Count == 0)
			Decide(settlement);
		else
			settlement.Timer = Context.Clock.CreateTimer(_ => Fire(() => DecideAfterWait(settlement)), null,
				wait, Timeout.InfiniteTimeSpan);
	}

	internal void DecideForClosing()
	{
		if (Rounds.Settlement is { Closed: false, Round.EndedAt: not null } settlement)
			Decide(settlement);
	}

	private void DecideAfterWait(RoundSettlement settlement)
	{
		lock (_sync)
		{
			if (IsClosed || settlement.Closed) return;

			Decide(settlement);
		}
	}

	private void Decide(RoundSettlement settlement)
	{
		var missing = settlement.Missing();
		settlement.Close();
		if (!settlement.HasParticipants) return;

		var result = RoundResult.Decide(settlement.Round, settlement.Scores);
		settlement.Round.Result = result;
		Context.Events.Publish(new RoomRoundDecided(this, settlement.Round, result, missing));
	}
}