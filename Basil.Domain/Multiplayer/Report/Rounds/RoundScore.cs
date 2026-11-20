using Basil.Domain.Game;
using Basil.Domain.Scores;

namespace Basil.Domain.Multiplayer.Report.Rounds;

/// <summary>A score submitted in a round, with the team the player was on.</summary>
public readonly record struct RoundScore
{
	/// <summary>Creates a round score.</summary>
	/// <param name="score">The score.</param>
	/// <param name="round">The round it was played in.</param>
	/// <param name="team">The player's team; required in a team mode and absent otherwise.</param>
	/// <exception cref="ArgumentException">A team is given in head-to-head mode, or missing in a team mode.</exception>
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

	/// <summary>Gets the score.</summary>
	public ScoreRecord Score { get; }

	/// <summary>Gets the round the score was played in.</summary>
	public Round Round { get; }

	/// <summary>Gets the player's team, or <see langword="null"/> in head-to-head mode.</summary>
	public GameTeam? Team { get; }
}