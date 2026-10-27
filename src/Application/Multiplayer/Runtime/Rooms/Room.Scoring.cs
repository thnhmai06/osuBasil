using Basil.Application.Common;
using Basil.Application.Users.Connections;
using Basil.Application.Multiplayer.Runtime.Events;
using Basil.Domain.Common;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Runtime.Rooms;

/// <content>Taking the scores of a round and deciding it.</content>
public sealed partial class Room
{
	/// <summary>Offers a stored score to this room as the score of a player of its latest round.</summary>
	/// <param name="score">The stored score.</param>
	/// <param name="player">The player who set it.</param>
	/// <returns>
	///     Ok when the score was taken as a score of the latest round; RoundMismatch when the round does not await a
	///     score like it from the player (another beatmap, a round already decided or aborted, a player who left before
	///     completing, or a second score); RoomClosed.
	/// </returns>
	/// <remarks>
	///     The round is decided as soon as it has ended and every player still awaited has a score; otherwise
	///     <see cref="RoomRounds.ScoreWait" /> after it ended, with the scores that came in.
	/// </remarks>
	internal Result<RoomOutcome> ReceiveScore(ScoreRecord score, UserRecord player)
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

	/// <summary>Tells whether the latest round awaits a score from a player on a beatmap.</summary>
	/// <param name="player">The player.</param>
	/// <param name="beatmap">The hash of the beatmap.</param>
	/// <returns><see langword="true" /> when the round has not been decided or aborted and awaits such a score.</returns>
	internal bool AwaitsScore(UserRecord player, Md5 beatmap)
	{
		lock (_sync)
		{
			return !IsClosed && Rounds.Settlement is { } settlement && settlement.Round.BeatmapHash == beatmap &&
			       settlement.Awaits(player, out _);
		}
	}

	/// <summary>Starts the settlement of a round with the players who took part in it.</summary>
	/// <param name="round">The round that started.</param>
	/// <param name="players">The players taking part.</param>
	private void BeginSettlement(Round round, IReadOnlyList<BanchoConnection> players)
	{
		DecideForClosing();
		Rounds.Settlement?.Close();
		Rounds.Settlement = new RoundSettlement(round,
			players.Select(player => (player.Record, Slots.Find(player)?.Team)));
	}

	/// <summary>Settles a round that ended and was not aborted: it is decided now, or after the wait for its scores.</summary>
	private void SettleRound()
	{
		if (Rounds.Settlement is not { Closed: false } settlement || settlement.Round.EndedAt is null) return;

		if (settlement.Missing().Count == 0)
			Decide(settlement);
		else
			settlement.Timer = Context.Clock.CreateTimer(_ => Fire(() => DecideAfterWait(settlement)), null,
				RoomRounds.ScoreWait, Timeout.InfiniteTimeSpan);
	}

	/// <summary>Decides a round that ended and still waits for scores, because the room closes or the next round starts.</summary>
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
		settlement.Round.Decision = result?.Verdict;
		Context.Events.Publish(new RoomRoundDecided(this, settlement.Round, result, missing));
	}
}