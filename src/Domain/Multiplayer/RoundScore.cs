using Basil.Domain.Mechanics;
using Basil.Domain.Scores;

namespace Basil.Domain.Multiplayer;

/// <summary>The relation between a round and a score played in it, with the team it was set for.</summary>
public sealed class RoundScore : IEquatable<RoundScore>
{
	/// <summary>Gets the stored score.</summary>
	public required ScoreRecord Score { get; init; }

	/// <summary>Gets the round the score was played in.</summary>
	/// <exception cref="ArgumentException">
	///     The team already assigned does not match the round: a team-versus round requires a team, and a
	///     head-to-head round forbids one.
	/// </exception>
	public required Round Round
	{
		get;
		init
		{
			field = value;
			if (_teamAssigned) ValidateTeam(value, _team, nameof(value));
		}
	}

	/// <summary>Gets the team the score was set for, or <see langword="null" /> in a round without teams.</summary>
	/// <exception cref="ArgumentException">
	///     The team presence does not match the round: a team-versus round requires a team, and a
	///     head-to-head round forbids one. The check runs once both the round and the team are set,
	///     whichever is set first.
	/// </exception>
	public required GameTeam? Team
	{
		get => _team;
		init
		{
			_team = value;
			_teamAssigned = true;
			if (Round is not null) ValidateTeam(Round, value, nameof(value));
		}
	}

	private GameTeam? _team;
	private bool _teamAssigned;

	private static void ValidateTeam(Round round, GameTeam? team, string paramName)
	{
		var isTeamMode = round.Settings.TeamType.IsTeamMode();
		var isBelongToTeam = team is not null;

		if (isTeamMode != isBelongToTeam)
			throw new ArgumentException(
				isTeamMode
					? "All scores must belong to a team in team-versus mode."
					: "All scores must not belong to a team in head-to-head mode.",
				paramName);
	}

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