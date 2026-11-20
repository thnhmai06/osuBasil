using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Basil.Domain.Game;
using Basil.Shared.Utilities;

namespace Basil.Domain.Multiplayer.Report.Rounds;

/// <summary>The outcome of a round: who won and by how much.</summary>
public abstract class RoundResult : IEquatable<RoundResult>
{
	/// <summary>Gets the round the result belongs to.</summary>
	public required Round Round { get; init; }

	/// <summary>Gets the difference in the win condition's metric between the winner and the runner-up; 0 for a draw.</summary>
	public double WinMargin { get; protected init; }

	/// <summary>Gets whether no side won.</summary>
	public bool IsDraw { get; protected init; }

	/// <summary>Gets the scores of the round in ranking order.</summary>
	public abstract IEnumerable<RoundScore> Scores { get; }

	/// <summary>Decides the result of a round from its scores, as head to head or by team according to the round's settings.</summary>
	/// <param name="round">The round.</param>
	/// <param name="scores">The scores received.</param>
	/// <returns>The result, or <see langword="null"/> when there are no scores.</returns>
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

	/// <summary>Orders scores from best to worst by the win condition.</summary>
	/// <param name="scores">The scores.</param>
	/// <param name="condition">The win condition.</param>
	protected static List<RoundScore> SortedScoresOf(IReadOnlyCollection<RoundScore> scores, GameWinCondition condition)
	{
		return [.. scores.OrderBy(score => score.Score, condition.ToComparer())];
	}

	/// <summary>Gets how far the best score is ahead of the runner-up in the win condition's metric; differences that are nearly zero count as 0.</summary>
	/// <param name="best">The best score.</param>
	/// <param name="runnerUp">The second-best score.</param>
	/// <param name="condition">The win condition.</param>
	protected static double MarginOf(RoundScore best, RoundScore runnerUp, GameWinCondition condition)
	{
		var difference = condition.GetMetricScore(best.Score.Value) - condition.GetMetricScore(runnerUp.Score.Value);
		return difference.NearlyEqual(0) ? 0 : difference;
	}
}

/// <summary>The result of a round where every player competes alone.</summary>
public sealed class HeadToHeadResult : RoundResult
{
	/// <summary>Creates a head-to-head result.</summary>
	/// <param name="round">The round.</param>
	/// <param name="scores">The scores of the round.</param>
	/// <param name="winMargin">How far the winner is ahead.</param>
	/// <param name="isDraw">Whether no player won.</param>
	/// <param name="winnerId">The id of the winning user, or <see langword="null"/> for a draw.</param>
	/// <exception cref="ArgumentException">The round is in a team mode, or a score belongs to another round.</exception>
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

	/// <summary>Gets the scores of the round, the best play first by the win condition.</summary>
	public override IEnumerable<RoundScore> Scores { get; }

	/// <summary>Gets the id of the winning user, or <see langword="null"/> for a draw.</summary>
	public int? WinnerId { get; }

	/// <summary>Decides a head-to-head round: passed plays rank ahead of failed ones, and the best play wins unless it ties the runner-up; a tie in combo, or in accuracy at 100%, goes to the higher total score.</summary>
	/// <param name="round">The round.</param>
	/// <param name="scores">The scores received.</param>
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
		// Equal combo, or equal accuracy at 100%, goes to the higher total score.
		var tieBroken = rankedCount > 1 && margin.NearlyEqual(0) && condition.BreaksTieByScore(ordered[0].Score.Value) &&
		                ordered[0].Score.Value.LastState.TotalScore != ordered[1].Score.Value.LastState.TotalScore;
		var winnerId = rankedCount != 0 && (rankedCount == 1 || tieBroken || !margin.NearlyEqual(0))
			? ordered[0].Score.Value.UserId
			: null;

		return new HeadToHeadResult(round, scores, margin, winnerId is null, winnerId);
	}
}

/// <summary>The result of a round played between teams.</summary>
public sealed class TeamVersusResult : RoundResult
{
	private static readonly GameTeam[] Teams = Enum.GetValues<GameTeam>();

	/// <summary>Creates a team result.</summary>
	/// <param name="round">The round.</param>
	/// <param name="winMargin">How far the winning team is ahead.</param>
	/// <param name="isDraw">Whether no team won.</param>
	/// <param name="winner">The winning team, or <see langword="null"/> for a draw.</param>
	/// <param name="teamScores">The score of every team.</param>
	/// <exception cref="ArgumentException">The round is not in a team mode, or a team has no score entry.</exception>
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

	/// <summary>Gets the score of every team.</summary>
	public FrozenDictionary<GameTeam, RoundTeamScore> TeamScores { get; }

	// The winner first, then the rest in enum order (a draw is all in enum order).
	public override IEnumerable<RoundScore> Scores => TeamScores
		.OrderBy(team => team.Key == Winner ? 0 : 1)
		.ThenBy(team => team.Key)
		.SelectMany(team => team.Value.Select(score => new RoundScore(score, Round, team.Key)));

	/// <summary>Gets the winning team, or <see langword="null"/> for a draw.</summary>
	public GameTeam? Winner { get; }

	/// <summary>Decides a team round: a team's value counts only its players who passed, the team ahead wins, equal values are a draw, and a team that is alone with scores wins.</summary>
	/// <param name="round">The round.</param>
	/// <param name="scores">The scores received.</param>
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
				margin = teamScores[winner.Value].PassedScore;
				break;
			case > 1:
			{
				var first = teamScores[rankedTeams[0]];
				var second = teamScores[rankedTeams[1]];
				// Only players who passed count towards their team.
				var difference = first.PassedScore - second.PassedScore;
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