using System.Collections;
using System.Collections.Immutable;
using Basil.Domain.Game;
using Basil.Domain.Scores;

namespace Basil.Domain.Multiplayer.Report.Rounds;

/// <summary>The scores of one team in a round, ordered by the win condition, with the team's passed and total score.</summary>
public sealed class RoundTeamScore(
	GameWinCondition condition,
	IReadOnlyCollection<ScoreRecord> scores,
	double passedScore,
	double totalScore) : IReadOnlySet<ScoreRecord>
{
	private readonly ImmutableSortedSet<ScoreRecord> _scores =
		ImmutableSortedSet.CreateRange(condition.ToComparer(), scores);

	/// <summary>Gets the team's metric over its passed plays; 0 when none passed.</summary>
	public double PassedScore { get; } = passedScore;

	/// <summary>Gets the team's metric over all its plays; 0 when it has none.</summary>
	public double TotalScore { get; } = totalScore;

	public IEnumerator<ScoreRecord> GetEnumerator()
	{
		return _scores.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	public int Count => _scores.Count;

	public bool Contains(ScoreRecord item)
	{
		return _scores.Contains(item);
	}

	public bool IsProperSubsetOf(IEnumerable<ScoreRecord> other)
	{
		return _scores.IsProperSubsetOf(other);
	}

	public bool IsProperSupersetOf(IEnumerable<ScoreRecord> other)
	{
		return _scores.IsProperSupersetOf(other);
	}

	public bool IsSubsetOf(IEnumerable<ScoreRecord> other)
	{
		return _scores.IsSubsetOf(other);
	}

	public bool IsSupersetOf(IEnumerable<ScoreRecord> other)
	{
		return _scores.IsSupersetOf(other);
	}

	public bool Overlaps(IEnumerable<ScoreRecord> other)
	{
		return _scores.Overlaps(other);
	}

	public bool SetEquals(IEnumerable<ScoreRecord> other)
	{
		return _scores.SetEquals(other);
	}

	/// <summary>Builds the score of one team from its plays.</summary>
	/// <param name="condition">The win condition that orders and measures the plays.</param>
	/// <param name="scores">The plays of the team.</param>
	public static RoundTeamScore Decide(GameWinCondition condition, IReadOnlyCollection<ScoreRecord> scores)
	{
		var all = scores.Select(score => score.Value).ToArray();
		var totalScore = all.Length == 0 ? 0 : condition.CalculateMetricScores(all);

		var passed = all.Where(score => score.IsPassed).ToArray();
		var passedScore = passed.Length == 0 ? 0 : condition.CalculateMetricScores(passed);

		return new RoundTeamScore(condition, scores, passedScore, totalScore);
	}
}