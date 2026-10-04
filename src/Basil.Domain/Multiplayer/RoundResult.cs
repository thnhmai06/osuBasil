using Basil.Domain.Mechanics;
using Basil.Domain.Scores;

namespace Basil.Domain.Multiplayer;

/// <summary>Who won a round and by how much.</summary>
/// <param name="WinningTeam">
///     The winning team in a team round, or <see langword="null" /> for a round without teams or a
///     draw.
/// </param>
/// <param name="WinningUserId">
///     The id of the winning player, or <see langword="null" /> when a team beat another team or
///     the round is a draw.
/// </param>
/// <param name="Margin">
///     How far the winner is ahead of the runner-up in the round's win metric; zero for a draw or an
///     unopposed winner.
/// </param>
public sealed record RoundResult(GameTeam? WinningTeam, int? WinningUserId, double Margin)
{
	/// <summary>Decides the result of a round from its scores.</summary>
	/// <param name="round">The round.</param>
	/// <param name="scores">The scores submitted in the round.</param>
	/// <returns>The result, or <see langword="null" /> when the round has no score.</returns>
	/// <remarks>
	///     The win metric follows the round's win condition: accuracy, maximum combo, or total score. A round is a team
	///     round when any score has a team; teams compare the sum of their players' metrics. A single score wins
	///     unopposed, with its team in a team round. Equal top metrics are a draw.
	/// </remarks>
	public static RoundResult? Decide(Round round, IReadOnlyList<Score> scores)
	{
		if (scores.Count == 0) return null;

		var condition = round.Settings.WinCondition;
		if (scores.Count == 1)
			return new RoundResult(scores[0].Value.Team, scores[0].Value.UserId, 0);

		if (scores.Any(s => s.Value.Team is not null))
		{
			var teams = scores
				.Where(s => s.Value.Team is not null)
				.GroupBy(s => s.Value.Team!.Value)
				.Select(g => (Team: g.Key, Total: g.Sum(s => Metric(s.Value, condition))))
				.OrderByDescending(t => t.Total)
				.ToList();

			if (teams.Count == 1) return new RoundResult(teams[0].Team, null, 0);
			return teams[0].Total == teams[1].Total
				? new RoundResult(null, null, 0)
				: new RoundResult(teams[0].Team, null, teams[0].Total - teams[1].Total);
		}

		var players = scores
			.Select(s => (s.Value.UserId, Metric: Metric(s.Value, condition)))
			.OrderByDescending(p => p.Metric)
			.ToList();

		return players[0].Metric == players[1].Metric
			? new RoundResult(null, null, 0)
			: new RoundResult(null, players[0].UserId, players[0].Metric - players[1].Metric);
	}

	private static double Metric(ScoreData score, GameWinCondition condition)
	{
		return condition switch
		{
			GameWinCondition.Accuracy => score.Accuracy,
			GameWinCondition.Combo => score.MaxCombo,
			_ => score.TotalScore
		};
	}
}