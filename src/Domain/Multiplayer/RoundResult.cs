using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Domain.Shared;

namespace Basil.Domain.Multiplayer;

public abstract class RoundResult : IEquatable<RoundResult>
{
	public required Round Round { get; init; }

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

	public double Margin { get; protected init; }

	public bool IsDraw { get; protected init; }

	public bool Equals(RoundResult? other)
	{
		return other is not null && Round.Equals(other.Round);
	}

	public static RoundResult? Decide(Round round, IReadOnlyCollection<RoundScore> scores)
	{
		return scores.Count == 0
			? null
			: round.Settings.TeamType.IsTeamMode()
				? new TeamVersusResult(round, scores)
				: new HeadToHeadResult(round, scores);
	}

	public static RoundResult Restore(Round round, IReadOnlyCollection<RoundScore> scores, IReadOnlyList<int> order,
		double margin, bool isDraw, int? winnerId, GameTeam? winner, double? redScore, double? blueScore)
	{
		return round.Settings.TeamType.IsTeamMode()
			? new TeamVersusResult(round, scores, order, margin, isDraw, winner, redScore, blueScore)
			: new HeadToHeadResult(round, scores, order, margin, isDraw, winnerId);
	}

	protected static List<RoundScore> InOrder(IReadOnlyCollection<RoundScore> scores, IReadOnlyList<int> order)
	{
		var position = order.Select((id, index) => (id, index)).ToDictionary(entry => entry.id, entry => entry.index);
		return [.. scores.OrderBy(score => position.GetValueOrDefault(score.Score.Id, order.Count))];
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

	protected static double MarginOf(RoundScore best, RoundScore runnerUp, GameWinCondition condition)
	{
		var difference = condition.Metric(best.Score.Value) - condition.Metric(runnerUp.Score.Value);
		return difference.NearlyEqual(0) ? 0 : difference;
	}
}

public sealed class HeadToHeadResult : RoundResult
{
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

	[SetsRequiredMembers]
	internal HeadToHeadResult(Round round, IReadOnlyCollection<RoundScore> scores, IReadOnlyList<int> order,
		double margin, bool isDraw, int? winnerId)
	{
		Round = round;
		Scores = InOrder(scores, order);
		Margin = margin;
		IsDraw = isDraw;
		WinnerId = winnerId;
	}

	public int? WinnerId { get; }
}

public sealed class TeamVersusResult : RoundResult
{
	private static readonly GameTeam[] Teams = Enum.GetValues<GameTeam>();

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

	[SetsRequiredMembers]
	internal TeamVersusResult(Round round, IReadOnlyCollection<RoundScore> scores, IReadOnlyList<int> order,
		double margin, bool isDraw, GameTeam? winner, double? redScore, double? blueScore)
	{
		Round = round;
		Scores = InOrder(scores, order);
		TeamScores = ScoresByTeam(scores);
		TeamScore = new Dictionary<GameTeam, double>
		{
			[GameTeam.Red] = redScore ?? 0,
			[GameTeam.Blue] = blueScore ?? 0
		}.ToFrozenDictionary();
		Margin = margin;
		IsDraw = isDraw;
		Winner = winner;
	}

	public FrozenDictionary<GameTeam, IReadOnlyList<ScoreRecord>> TeamScores { get; }

	public FrozenDictionary<GameTeam, double> TeamScore { get; }

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

	private static IOrderedEnumerable<(GameTeam Team, double Total)> Rank(
		IEnumerable<RoundScore> scores, GameWinCondition condition)
	{
		return scores
			.GroupBy(s => s.Team!.Value)
			.Select(g => (Team: g.Key, Total: Total(g, condition)))
			.OrderByDescending(t => t.Total);
	}

	private static double Total(IEnumerable<RoundScore> team, GameWinCondition condition)
	{
		return condition switch
		{
			GameWinCondition.Accuracy => team.Average(s => condition.Metric(s.Score.Value)),
			_ => team.Sum(s => condition.Metric(s.Score.Value))
		};
	}
}