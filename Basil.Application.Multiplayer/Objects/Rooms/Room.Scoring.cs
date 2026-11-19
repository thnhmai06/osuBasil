using Basil.Application.Multiplayer.Objects.Events;
using Basil.Domain.Scores;
using Basil.Domain.Multiplayer.Room;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed partial class Room
{
	internal RoomOutcome ReceiveScore(ScoreRecord score, UserRecord player)
	{
		using (State.Enter())
		{
			var (outcome, settlement, roundScore) = State.ReceiveScore(score, player);
			if (outcome is not RoomOutcome.Ok) return outcome;

			Context.Events.Publish(new RoomRoundScoreSubmitted(this, settlement!.Round, player, roundScore!.Value));
			if (settlement.Round.EndedAt is not null && settlement.Missing().Count == 0) Decide(settlement);
			return RoomOutcome.Ok;
		}
	}

	internal bool AwaitsScore(UserRecord player, Md5Hash beatmap)
	{
		using (State.Enter())
		{
			return State.AwaitsScore(player, beatmap);
		}
	}

	private void SettleRound()
	{
		if (Rounds.Settlement is not { Closed: false } settlement || settlement.Round.EndedAt is null) return;

		var wait = Context.Settings.Current.Multiplayer.Rounds.ScoreWait;
		if (settlement.HasCompletions)
			Context.Events.Publish(new RoomRoundEnded(this, settlement.Round, wait));

		if (settlement.Missing().Count == 0)
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
		Publish(State.DecideForClosing());
		SyncTimer();
	}

	private void DecideAfterWait(RoundSettlement settlement)
	{
		using (State.Enter())
		{
			if (IsClosed || settlement.Closed) return;

			Decide(settlement);
		}
	}

	private void Decide(RoundSettlement settlement)
	{
		var decision = Domain.Multiplayer.Room.Room.Decide(settlement);
		SyncTimer();
		Publish(decision);
	}

	private void Publish(RoundDecision? decision)
	{
		if (decision is { } d)
			Context.Events.Publish(new RoomRoundDecided(this, d.Round, d.Result, d.Missing));
	}
}