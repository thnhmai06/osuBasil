using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Domain.Utilities;

namespace Basil.Domain.Multiplayer.Round;

/// <summary>Who won a round, and by how much, decided from the scores played in it.</summary>
public abstract class RoundResult : IEquatable<RoundResult>
{
	/// <summary>Gets the round the result was decided for.</summary>
	public required Round Round { get; init; }

	/// <summary>Gets the scores the result was decided from, the winner first.</summary>
	/// <exception cref="ArgumentException">A score does not belong to <see cref="Round" />.</exception>
	public required IReadOnlyList<RoundScore> Scores
	{
		get;
		init
		{
			if (!value.Any(s => s.Round.Equals(Round)))
				throw new ArgumentException("All scores must belong to this round.", nameof(value));
			field = value;
		}
	}

	/// <summary>
	///     Gets how far the winner is ahead of the runner-up in the round's win metric; zero for a draw.
	///     An opponent that did not play counts as zero.
	/// </summary>
	public double Margin { get; protected init; }

	/// <summary>Gets a value that indicates whether the round is a draw, with no winner ahead.</summary>
	public bool IsDraw => Margin.NearlyEqual(0);

	/// <summary>Determines whether another result was decided for the same round.</summary>
	/// <param name="other">The result to compare against, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="other" /> belongs to the same <see cref="Round" />;
	///     otherwise, <see langword="false" />.
	/// </returns>
	public bool Equals(RoundResult? other)
	{
		return other is not null && Round.Equals(other.Round);
	}

	/// <summary>Determines whether this result equals another object.</summary>
	/// <param name="obj">The object to compare against, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="obj" /> is a <see cref="RoundResult" /> for the same
	///     round; otherwise, <see langword="false" />.
	/// </returns>
	public override bool Equals(object? obj)
	{
		if (obj is null) return false;
		return obj is RoundResult result && Equals(result);
	}

	/// <summary>Returns a hash code consistent with the result's equality.</summary>
	/// <returns>The hash code of the round the result was decided for.</returns>
	public override int GetHashCode()
	{
		return Round.GetHashCode();
	}

	/// <summary>Gets the margin between two scores in the round's win metric.</summary>
	/// <param name="best">The higher-ranked score.</param>
	/// <param name="runnerUp">The score behind it.</param>
	/// <param name="condition">The win condition that selects the metric.</param>
	/// <returns>The gap between the two, or zero when they tie.</returns>
	protected static double MarginOf(RoundScore best, RoundScore runnerUp, GameWinCondition condition)
	{
		var difference = condition.Metric(best.Score.Value) - condition.Metric(runnerUp.Score.Value);
		return difference.NearlyEqual(0) ? 0 : difference;
	}
}

/// <summary>Who won a round played without teams, and by how much.</summary>
public sealed class HeadToHeadResult : RoundResult
{
	/// <summary>
	///     Gets the id of the winning player, or <see langword="null" /> for a draw or when the round has
	///     no score.
	/// </summary>
	public readonly int? WinnerId;

	/// <summary>Decides which player wins a head-to-head round from its scores.</summary>
	/// <param name="round">The round.</param>
	/// <param name="scores">The scores submitted in the round.</param>
	/// <exception cref="ArgumentException">The round is played in a team mode.</exception>
	[SetsRequiredMembers]
	public HeadToHeadResult(Round round, IReadOnlyCollection<RoundScore> scores)
	{
		Round = round;

		if (round.Settings.TeamType.IsTeamMode())
			throw new ArgumentException("A head-to-head round result requires head-to-head-mode round.", nameof(round));

		var condition = round.Settings.WinCondition;
		var comparer = condition.ToComparer();

		// Players still alive at the end rank first; a failed score ranks at the bottom, sorted apart.
		var passed = scores
			.Where(s => s.Score.Value.IsPassed)
			.OrderByDescending(s => s.Score.Value, comparer)
			.ToList();
		var failed = scores
			.Where(s => !s.Score.Value.IsPassed)
			.OrderByDescending(s => s.Score.Value, comparer)
			.ToList();

		// When everyone failed, the failed scores still decide the winner.
		var contenders = passed.Count > 0 ? passed : failed;

		Scores = passed.Concat(failed).ToList();
		Margin = contenders.Count switch
		{
			0 => 0,
			1 => condition.Metric(contenders[0].Score.Value),
			_ => MarginOf(contenders[0], contenders[1], condition)
		};
		WinnerId = contenders.Count != 0 && (contenders.Count <= 1 || !IsDraw)
			? contenders[0].Score.Value.UserId
			: null;
	}
}

/// <summary>Who won a team round, and by how much.</summary>
public sealed class TeamVersusResult : RoundResult
{
	private static readonly GameTeam[] Teams = Enum.GetValues<GameTeam>();

	/// <summary>
	///     Gets the scores each team set, with a team that played none present as an empty sequence.
	/// </summary>
	public readonly FrozenDictionary<GameTeam, IEnumerable<Score>> TeamScores;

	/// <summary>
	///     Gets the win metric each team reached — the sum of its scores or combos, or its average
	///     accuracy — with a team that has no score counting as zero.
	/// </summary>
	public readonly FrozenDictionary<GameTeam, double> TeamScore;

	/// <summary>
	///     Gets the winning team, or <see langword="null" /> for a round without teams or a draw.
	/// </summary>
	public readonly GameTeam? Winner;

	/// <summary>Decides which team wins a team round from its scores.</summary>
	/// <param name="round">The team round.</param>
	/// <param name="scores">The scores submitted in the round.</param>
	/// <exception cref="ArgumentException">The round is not played in a team mode.</exception>
	[SetsRequiredMembers]
	public TeamVersusResult(Round round, IReadOnlyCollection<RoundScore> scores)
	{
		Round = round;

		if (!round.Settings.TeamType.IsTeamMode())
			throw new ArgumentException("A team round result requires team-mode round.", nameof(round));

		var condition = round.Settings.WinCondition;

		// Every team is present, even one with no score; an absent team counts as zero.
		TeamScores = Teams.ToFrozenDictionary(
			team => team,
			team => scores.Where(s => s.Team == team).Select(s => s.Score));

		// Only teams with a player still alive at the end contend; a team whose players all failed drops
		// out and the opposing team wins unopposed.
		var teams = Rank(scores.Where(s => s.Score.Value.IsPassed), condition).ToList();

		// When every team failed, the failed scores still decide the winner.
		if (teams.Count == 0) teams = [.. Rank(scores, condition)];

		TeamScore = Teams.ToFrozenDictionary(
			team => team,
			team => teams.FirstOrDefault(t => t.Team == team).Total);

		Winner = teams.Count == 0 || (teams.Count > 1 && teams[0].Total.NearlyEqual(teams[1].Total))
			? null
			: teams[0].Team;
		Margin = teams.Count switch
		{
			0 => 0,
			1 => teams[0].Total,
			_ => teams[0].Total.NearlyEqual(teams[1].Total) ? 0 : teams[0].Total - teams[1].Total
		};

		// The winning team's scores come first; within a team the passed players come first, each as a
		// head-to-head round, the highest metric first.
		var rank = teams
			.Select((t, index) => (t.Team, Index: index))
			.ToDictionary(t => t.Team, t => t.Index);
		var comparer = condition.ToComparer();

		Scores = scores
			.OrderBy(s => rank.TryGetValue(s.Team!.Value, out var index) ? index : teams.Count)
			.ThenByDescending(s => s.Score.Value.IsPassed)
			.ThenByDescending(s => s.Score.Value, comparer)
			.ToList();
	}

	/// <summary>Ranks teams by their win metric, the team that is ahead first.</summary>
	private static IOrderedEnumerable<(GameTeam Team, double Total)> Rank(
		IEnumerable<RoundScore> scores, GameWinCondition condition)
	{
		return scores
			.GroupBy(s => s.Team!.Value)
			.Select(g => (Team: g.Key, Total: Total(g, condition)))
			.OrderByDescending(t => t.Total);
	}

	/// <summary>
	///     Gets a team's win metric: the sum of its players' scores or combos, or their average accuracy.
	/// </summary>
	/// <remarks>The caller passes only the scores that contribute to the metric.</remarks>
	private static double Total(IEnumerable<RoundScore> team, GameWinCondition condition)
	{
		return condition switch
		{
			GameWinCondition.Accuracy => team.Average(s => condition.Metric(s.Score.Value)),
			_ => team.Sum(s => condition.Metric(s.Score.Value))
		};
	}
}
