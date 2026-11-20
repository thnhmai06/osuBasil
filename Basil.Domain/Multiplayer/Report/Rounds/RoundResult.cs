using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Basil.Domain.Game;
using Basil.Shared.Utilities;

namespace Basil.Domain.Multiplayer.Report.Rounds;

public abstract class RoundResult : IEquatable<RoundResult>
{
	public required Round Round { get; init; }

	public double WinMargin { get; protected init; }

	public bool IsDraw { get; protected init; }

	public abstract IEnumerable<RoundScore> Scores { get; }

	public static RoundResult? Decide(Round round, IReadOnlyCollection<RoundScore> scores)
	{
		return scores.Count == 0
			? null
			: round.Settings.TeamType.IsTeamMode()
				? TeamVersusResult.Decide(round, scores)
				: HeadToHeadResult.Decide(round, scores);
	}

	public bool Equals(RoundResult? other)
	{
		return other is not null && Round.Equals(other.Round);
	}

	public override bool Equals(object? obj)
	{
		if (obj is null) return false;
		return obj is RoundResult result && Equals(result);
	}

	public override int GetHashCode()
	{
		return Round.GetHashCode();
	}

	protected static List<RoundScore> SortedScoresOf(IReadOnlyCollection<RoundScore> scores, GameWinCondition condition)
	{
		return [.. scores.OrderBy(score => score.Score, condition.ToComparer())];
	}

	protected static double MarginOf(RoundScore best, RoundScore runnerUp, GameWinCondition condition)
	{
		var difference = condition.GetMetricScore(best.Score.Value) - condition.GetMetricScore(runnerUp.Score.Value);
		return difference.NearlyEqual(0) ? 0 : difference;
	}
}

public sealed class HeadToHeadResult : RoundResult
{
	[SetsRequiredMembers]
	public HeadToHeadResult(
		Round round, IReadOnlyCollection<RoundScore> scores, double winMargin, bool isDraw, int? winnerId)
	{
		Round = round;

		if (round.Settings.TeamType.IsTeamMode())
			throw new ArgumentException("A head-to-head round result requires head-to-head-mode round.", nameof(round));

		if (scores.Any(score => !score.Round.Equals(round)))
			throw new ArgumentException("All scores must belong to this round.", nameof(scores));

		Scores = SortedScoresOf(scores, round.Settings.WinCondition);
		WinMargin = winMargin;
		IsDraw = isDraw;
		WinnerId = winnerId;
	}

	public override IEnumerable<RoundScore> Scores { get; }

	public int? WinnerId { get; }

	internal new static HeadToHeadResult Decide(Round round, IReadOnlyCollection<RoundScore> scores)
	{
		var condition = round.Settings.WinCondition;
		var ordered = SortedScoresOf(scores, condition);
		// Passed plays sort first, so the ranked plays are a prefix of the order.
		var passedCount = ordered.Count(score => score.Score.Value.IsPassed);
		var rankedCount = passedCount > 0 ? passedCount : ordered.Count;

		var margin = rankedCount switch
		{
			0 => 0,
			1 => condition.GetMetricScore(ordered[0].Score.Value),
			_ => MarginOf(ordered[0], ordered[1], condition)
		};
		var winnerId = rankedCount != 0 && (rankedCount == 1 || !margin.NearlyEqual(0))
			? ordered[0].Score.Value.UserId
			: null;

		return new HeadToHeadResult(round, scores, margin, winnerId is null, winnerId);
	}
}

public sealed class TeamVersusResult : RoundResult
{
	private static readonly GameTeam[] Teams = Enum.GetValues<GameTeam>();

	[SetsRequiredMembers]
	public TeamVersusResult(Round round, double winMargin, bool isDraw, GameTeam? winner,
		IReadOnlyDictionary<GameTeam, RoundTeamScore> teamScores)
	{
		Round = round;

		if (!round.Settings.TeamType.IsTeamMode())
			throw new ArgumentException("A team round result requires team-mode round.", nameof(round));

		if (Teams.Any(team => !teamScores.ContainsKey(team)))
			throw new ArgumentException("A team round result requires a score for every team.", nameof(teamScores));

		TeamScores = teamScores.ToFrozenDictionary();
		Winner = winner;
		WinMargin = winMargin;
		IsDraw = isDraw;
	}

	public FrozenDictionary<GameTeam, RoundTeamScore> TeamScores { get; }

	// The winner first, then the rest in enum order (a draw is all in enum order).
	public override IEnumerable<RoundScore> Scores => TeamScores
		.OrderBy(team => team.Key == Winner ? 0 : 1)
		.ThenBy(team => team.Key)
		.SelectMany(team => team.Value.Select(score => new RoundScore(score, Round, team.Key)));

	public GameTeam? Winner { get; }

	internal new static TeamVersusResult Decide(Round round, IReadOnlyCollection<RoundScore> scores)
	{
		var condition = round.Settings.WinCondition;
		var teamScores = Teams.ToFrozenDictionary(
			team => team,
			team => RoundTeamScore.Decide(
				condition, scores.Where(score => score.Team == team).Select(score => score.Score).ToArray()));
		var rankedTeams = Teams.Where(team => teamScores[team].Count > 0).ToList();

		GameTeam? winner = null;
		double margin = 0;
		switch (rankedTeams.Count)
		{
			case 1:
				winner = rankedTeams[0];
				margin = teamScores[winner.Value].Any(score => score.Value.IsPassed)
					? teamScores[winner.Value].PassedScore
					: teamScores[winner.Value].TotalScore;
				break;
			case > 1:
			{
				var first = teamScores[rankedTeams[0]];
				var second = teamScores[rankedTeams[1]];
				// A difference in passed score decides; the total score only breaks a tie.
				var difference = first.PassedScore - second.PassedScore;
				if (difference.NearlyEqual(0)) difference = first.TotalScore - second.TotalScore;
				if (!difference.NearlyEqual(0))
				{
					winner = difference > 0 ? rankedTeams[0] : rankedTeams[1];
					margin = Math.Abs(difference);
				}

				break;
			}
		}

		return new TeamVersusResult(round, margin, winner is null, winner, teamScores);
	}
}