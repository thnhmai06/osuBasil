using System.Collections;
using System.Collections.Immutable;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;

namespace Basil.Domain.Multiplayer.Report.Rounds;

public sealed class RoundTeamScore(
	GameWinCondition condition,
	IReadOnlyCollection<ScoreRecord> scores,
	double passedScore,
	double totalScore) : IReadOnlySet<ScoreRecord>
{
	private readonly ImmutableSortedSet<ScoreRecord> _scores =
		ImmutableSortedSet.CreateRange(condition.ToComparer(), scores);

	public double PassedScore { get; } = passedScore;

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

	public static RoundTeamScore Decide(GameWinCondition condition, IReadOnlyCollection<ScoreRecord> scores)
	{
		var all = scores.Select(score => score.Value).ToArray();
		var totalScore = all.Length == 0 ? 0 : condition.CalculateMetricScores(all);

		var passed = all.Where(score => score.IsPassed).ToArray();
		var passedScore = passed.Length == 0 ? 0 : condition.CalculateMetricScores(passed);

		return new RoundTeamScore(condition, scores, passedScore, totalScore);
	}
}