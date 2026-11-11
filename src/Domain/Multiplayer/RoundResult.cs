using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;

namespace Basil.Domain.Multiplayer;

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
			if (value.Any(s => !s.Round.Equals(Round)))
				throw new ArgumentException("All scores must belong to this round.", nameof(value));
			field = value;
		}
	}

	/// <summary>
	///     Gets how far the winner is ahead of the runner-up in the round's win metric; zero for a draw.
	///     An opponent that did not play counts as zero.
	/// </summary>
	public double Margin { get; protected init; }

	/// <summary>
	///     Gets a value that indicates whether the round has no winner: the leading players or teams tie, or
	///     nobody scored. A sole contender wins even with a metric of zero.
	/// </summary>
	public bool IsDraw { get; protected init; }

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

	/// <summary>Decides who won a round from the scores played in it.</summary>
	/// <param name="round">The round.</param>
	/// <param name="scores">The scores submitted in the round.</param>
	/// <returns>
	///     A team result for a round played in teams, a head-to-head result otherwise, or <see langword="null" /> when
	///     the round has no score.
	/// </returns>
	public static RoundResult? Decide(Round round, IReadOnlyCollection<RoundScore> scores)
	{
		return scores.Count == 0
			? null
			: round.Settings.TeamType.IsTeamMode()
				? new TeamVersusResult(round, scores)
				: new HeadToHeadResult(round, scores);
	}

	/// <summary>Builds the result of a round from the decision that was stored for it, without deciding it again.</summary>
	/// <remarks>
	///     The ranking, the margin and the draw flag are the ones decided when the round finished; a later change
	///     of the rules does not alter them.
	/// </remarks>
	/// <param name="round">The round.</param>
	/// <param name="scores">The scores of the round.</param>
	/// <param name="order">
	///     The ids of the round's scores in the order decided when the round finished, the winner first. A score
	///     it does not name comes last.
	/// </param>
	/// <param name="margin">How far the winner was ahead of the runner-up when the round finished.</param>
	/// <param name="isDraw">Whether the round finished without a winner.</param>
	/// <returns>The result, with the scores in the stored order.</returns>
	public static RoundResult Restore(Round round, IReadOnlyCollection<RoundScore> scores, IReadOnlyList<int> order,
		double margin, bool isDraw)
	{
		return round.Settings.TeamType.IsTeamMode()
			? new TeamVersusResult(round, scores, order, margin, isDraw)
			: new HeadToHeadResult(round, scores, order, margin, isDraw);
	}

	/// <summary>Puts scores in the order a decision stored, scores it does not name last.</summary>
	/// <param name="scores">The scores.</param>
	/// <param name="order">The stored order of score ids.</param>
	protected static List<RoundScore> InOrder(IReadOnlyCollection<RoundScore> scores, IReadOnlyList<int> order)
	{
		var position = order.Select((id, index) => (id, index)).ToDictionary(entry => entry.id, entry => entry.index);
		return [.. scores.OrderBy(score => position.GetValueOrDefault(score.Score.Id, order.Count))];
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
		WinnerId = contenders.Count != 0 && (contenders.Count == 1 || !Margin.NearlyEqual(0))
			? contenders[0].Score.Value.UserId
			: null;
		IsDraw = WinnerId is null;
	}

	/// <summary>Builds a head-to-head result from the decision that was stored for it.</summary>
	/// <param name="round">The round.</param>
	/// <param name="scores">The scores of the round.</param>
	/// <param name="order">The stored order of score ids, the winner first.</param>
	/// <param name="margin">The stored margin.</param>
	/// <param name="isDraw">Whether the stored decision is a draw.</param>
	[SetsRequiredMembers]
	internal HeadToHeadResult(Round round, IReadOnlyCollection<RoundScore> scores, IReadOnlyList<int> order,
		double margin, bool isDraw)
	{
		Round = round;
		Scores = InOrder(scores, order);
		Margin = margin;
		IsDraw = isDraw;
		WinnerId = isDraw ? null : Scores.FirstOrDefault()?.Score.Value.UserId;
	}

	/// <summary>
	///     Gets the id of the winning player, or <see langword="null" /> for a draw or when the round has
	///     no score.
	/// </summary>
	public int? WinnerId { get; }
}

/// <summary>Who won a team round, and by how much.</summary>
public sealed class TeamVersusResult : RoundResult
{
	private static readonly GameTeam[] Teams = Enum.GetValues<GameTeam>();

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
		TeamScores = ScoresByTeam(scores);
		var teams = Contenders(scores, condition);
		TeamScore = TotalsOf(teams);

		Winner = teams.Count == 0 || (teams.Count > 1 && teams[0].Total.NearlyEqual(teams[1].Total))
			? null
			: teams[0].Team;
		IsDraw = Winner is null;
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

	/// <summary>Builds a team result from the decision that was stored for it.</summary>
	/// <param name="round">The team round.</param>
	/// <param name="scores">The scores of the round.</param>
	/// <param name="order">The stored order of score ids, the winning team first.</param>
	/// <param name="margin">The stored margin.</param>
	/// <param name="isDraw">Whether the stored decision is a draw.</param>
	[SetsRequiredMembers]
	internal TeamVersusResult(Round round, IReadOnlyCollection<RoundScore> scores, IReadOnlyList<int> order,
		double margin, bool isDraw)
	{
		Round = round;
		Scores = InOrder(scores, order);
		TeamScores = ScoresByTeam(scores);
		TeamScore = TotalsOf(Contenders(scores, round.Settings.WinCondition));
		Margin = margin;
		IsDraw = isDraw;
		Winner = isDraw ? null : Scores.FirstOrDefault()?.Team;
	}

	/// <summary>
	///     Gets the scores each team set, with a team that played none present as an empty sequence.
	/// </summary>
	public FrozenDictionary<GameTeam, IReadOnlyList<ScoreRecord>> TeamScores { get; }

	/// <summary>
	///     Gets the win metric each team reached — the sum of its scores or combos, or its average
	///     accuracy — with a team that has no score counting as zero.
	/// </summary>
	public FrozenDictionary<GameTeam, double> TeamScore { get; }

	/// <summary>
	///     Gets the winning team, or <see langword="null" /> for a round without teams or a draw.
	/// </summary>
	public GameTeam? Winner { get; }

	private static FrozenDictionary<GameTeam, IReadOnlyList<ScoreRecord>> ScoresByTeam(
		IReadOnlyCollection<RoundScore> scores)
	{
		return Teams.ToFrozenDictionary<GameTeam, GameTeam, IReadOnlyList<ScoreRecord>>(
			team => team,
			team => scores.Where(s => s.Team == team).Select(s => s.Score).ToList());
	}

	// Only teams with a player still alive at the end contend; a team whose players all failed drops out and the
	// opposing team wins unopposed. When every team failed, the failed scores still decide the winner.
	private static List<(GameTeam Team, double Total)> Contenders(IReadOnlyCollection<RoundScore> scores,
		GameWinCondition condition)
	{
		var teams = Rank(scores.Where(s => s.Score.Value.IsPassed), condition).ToList();
		return teams.Count == 0 ? [.. Rank(scores, condition)] : teams;
	}

	private static FrozenDictionary<GameTeam, double> TotalsOf(List<(GameTeam Team, double Total)> teams)
	{
		return Teams.ToFrozenDictionary(
			team => team,
			team => teams.FirstOrDefault(t => t.Team == team).Total);
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