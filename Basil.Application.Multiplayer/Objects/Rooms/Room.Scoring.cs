using Basil.Application.Multiplayer.Objects.Events;
using Basil.Domain.Scores;
using Basil.Domain.Multiplayer.Room;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed partial class Room
{
	internal RoomActionResult ReceiveScore(ScoreRecord score, UserRecord player)
	{
		using (Enter())
		{
			var (outcome, settlement, roundScore) = State.Rounds.ReceiveScore(score, player);
			if (outcome is not RoomActionResult.Ok) return outcome;

			Context.Events.Publish(new RoomRoundScoreSubmitted(this, settlement!.Round, player, roundScore!.Value));
			if (settlement.Round.EndedAt is not null && !settlement.Missing.Any()) Decide(settlement);
			return RoomActionResult.Ok;
		}
	}

	internal bool AwaitsScore(UserRecord player, Md5Hash beatmap)
	{
		using (Enter())
		{
			return State.Rounds.AwaitsScore(player, beatmap);
		}
	}

	private void SettleRound()
	{
		if (Rounds.Settlement is not { Closed: false } settlement || settlement.Round.EndedAt is null) return;

		var wait = Context.Settings.Current.Multiplayer.Rounds.ScoreWait;
		if (settlement.HasCompletions)
			Context.Events.Publish(new RoomRoundEnded(this, settlement.Round, wait));

		if (!settlement.Missing.Any())
		{
			Decide(settlement);
			return;
		}

		_timer = Context.Clock.CreateTimer(_ => Fire(() => DecideAfterWait(settlement)), null,
			wait, Timeout.InfiniteTimeSpan);
		_timerSettlement = settlement;
	}

	internal void DecideForClosing()
	{
		Publish(State.Rounds.DecideForClosing());
		SyncTimer();
	}

	private void DecideAfterWait(RoundSettlement settlement)
	{
		using (Enter())
		{
			if (IsClosed || settlement.Closed) return;

			Decide(settlement);
		}
	}

	private void Decide(RoundSettlement settlement)
	{
		var decision = Domain.Multiplayer.Room.RoomRounds.Decide(settlement);
		SyncTimer();
		Publish(decision);
	}

	private void Publish(RoundDecision? decision)
	{
		if (decision != null)
			Context.Events.Publish(new RoomRoundDecided(this, decision.Round, decision.Result, decision.Missing));
	}
}