using Basil.Domain.Mechanics;
using Basil.Domain.Scores;

namespace Basil.Domain.Multiplayer;

public sealed class RoundScore : IEquatable<RoundScore>
{
	public RoundScore(ScoreRecord score, Round round, GameTeam? team)
	{
		var isTeamMode = round.Settings.TeamType.IsTeamMode();
		if (isTeamMode != team is not null)
			throw new ArgumentException(
				isTeamMode
					? "All scores must belong to a team in team-versus mode."
					: "All scores must not belong to a team in head-to-head mode.",
				nameof(team));

		Score = score;
		Round = round;
		Team = team;
	}

	public ScoreRecord Score { get; }

	public Round Round { get; }

	public GameTeam? Team { get; }

	public bool Equals(RoundScore? other)
	{
		return other is not null && Score.Equals(other.Score);
	}

	public override bool Equals(object? obj)
	{
		return obj is RoundScore other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Score);
	}
}