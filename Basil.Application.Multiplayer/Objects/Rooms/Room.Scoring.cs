using Basil.Application.Multiplayer.Objects.Events;
using Basil.Domain.Multiplayer.Room;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Scores;
using Basil.Domain.Spectating;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed partial class Room
{
	/// <summary>Relays a player's score frame to the players and observers when the player sits in the room and a round is in progress.</summary>
	/// <returns>Whether the frame was relayed.</returns>
	internal bool RelayScore(Session actor, ScoreFrame frame)
	{
		if (actor is not StableSession player) return false;

		using (Enter())
		{
			if (SlotOf(player) is not { } slot || !Rounds.InProgress) return false;

			Emit(new RoomScoreRelayed(this, slot.Index, frame, [.. Players, .. Members.Observers]));
			return true;
		}
	}

	internal RoomActionResult ReceiveScore(ScoreRecord score, UserRecord player)
	{
		using (Enter())
		{
			var (outcome, settlement, roundScore) = State.Rounds.ReceiveScore(score, player);
			if (outcome is not RoomActionResult.Ok) return outcome;

			Context.Events.Publish(new RoomRoundScoreSubmitted(this, settlement!.Round, player, roundScore!.Value));
			if (settlement.Round.EndedAt is not null && !settlement.Missing.Any()) Decide();
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
			Decide();
			return;
		}

		_timer = Context.Clock.CreateTimer(_ => Fire(() => DecideAfterWait(settlement)), null,
			wait, Timeout.InfiniteTimeSpan);
		_timerSettlement = settlement;
	}

	internal void DecideForClosing()
	{
		Publish(State.Rounds.Settle().Decision);
		SyncTimer();
	}

	private void DecideAfterWait(RoundSettlement settlement)
	{
		using (Enter())
		{
			if (IsClosed || settlement.Closed) return;

			Decide();
		}
	}

	private void Decide()
	{
		var decision = State.Rounds.Settle().Decision;
		SyncTimer();
		Publish(decision);
	}

	private void Publish(RoundDecision? decision)
	{
		if (decision != null)
			Context.Events.Publish(new RoomRoundDecided(this, decision.Round, decision.Result, decision.Missing));
	}
}