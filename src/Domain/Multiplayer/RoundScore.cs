using Basil.Domain.Mechanics;
using Basil.Domain.Scores;

namespace Basil.Domain.Multiplayer;

/// <summary>The relation between a round and a score played in it, with the team it was set for.</summary>
public sealed class RoundScore : IEquatable<RoundScore>
{
	/// <summary>Creates the relation between a round and a score played in it.</summary>
	/// <param name="score">The stored score.</param>
	/// <param name="round">The round the score was played in.</param>
	/// <param name="team">The team the score was set for, or <see langword="null" /> in a round without teams.</param>
	/// <exception cref="ArgumentException">
	///     The team does not match the round: a team-versus round requires a team, and a head-to-head round forbids one.
	/// </exception>
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

	/// <summary>Gets the stored score.</summary>
	public ScoreRecord Score { get; }

	/// <summary>Gets the round the score was played in.</summary>
	public Round Round { get; }

	/// <summary>Gets the team the score was set for, or <see langword="null" /> in a round without teams.</summary>
	public GameTeam? Team { get; }

	/// <summary>Determines whether another entry refers to the same stored score.</summary>
	/// <param name="other">The entry to compare with, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="other" /> has the same <see cref="Score" />;
	///     otherwise, <see langword="false" />.
	/// </returns>
	public bool Equals(RoundScore? other)
	{
		return other is not null && Score.Equals(other.Score);
	}

	/// <summary>Determines whether this entry equals another object.</summary>
	/// <param name="obj">The object to compare with, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="obj" /> is a <see cref="RoundScore" /> with the same
	///     <see cref="Score" />; otherwise, <see langword="false" />.
	/// </returns>
	public override bool Equals(object? obj)
	{
		return obj is RoundScore other && Equals(other);
	}

	/// <summary>Returns a hash code consistent with the entry's equality.</summary>
	/// <returns>The hash code of the stored score.</returns>
	public override int GetHashCode()
	{
		return HashCode.Combine(Score);
	}
}