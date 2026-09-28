using Basil.Domain.Mechanics;

namespace Basil.Domain.Users;

/// <summary>
///     Represents a user's cumulative score statistics for one game mode, as reported to clients.
/// </summary>
/// <remarks>
///     Basil does not calculate accuracy, performance points, or leaderboard rank the way osu! does;
///     <see cref="Accuracy" />, <see cref="Pp" />, and <see cref="Rank" /> are fixed values clients
///     display in place of a real calculation.
/// </remarks>
public sealed class UserStats : IEquatable<UserStats>
{
	/// <summary>Gets the fixed accuracy value Basil reports to clients, in place of a real calculation.</summary>
	public static double Accuracy => 100.0;

	/// <summary>Gets the fixed performance-points value Basil reports to clients, in place of a real calculation.</summary>
	public static int Pp => 727;

	/// <summary>Gets the fixed leaderboard rank Basil reports to clients, in place of a real calculation.</summary>
	public int Rank => UserId;

	/// <summary>The identifier of the user these statistics belong to.</summary>
	public required int UserId { get; init; }

	/// <summary>The game mode these statistics are for.</summary>
	public required GameMode Mode { get; init; }

	/// <summary>The cumulative score across every submitted play.</summary>
	public long TotalScore { get; init; } = 0; // all scores

	/// <summary>The cumulative score across every submitted play on ranked beatmaps.</summary>
	public long RankedScore { get; init; } = 0; // multiplayer-only score

	/// <summary>The number of plays submitted.</summary>
	public int PlayCount { get; init; } = 0;

	/// <summary>Returns a copy of these statistics with a submitted score's totals added in.</summary>
	/// <param name="score">The score to add to <see cref="TotalScore" />.</param>
	/// <param name="rankedScore">The score to add to <see cref="RankedScore" />.</param>
	/// <returns>A new <see cref="UserStats" /> with the updated totals and an incremented <see cref="PlayCount" />.</returns>
	public UserStats WithSubmittedScore(long score, long rankedScore)
	{
		return new UserStats
		{
			UserId = UserId,
			Mode = Mode,
			TotalScore = TotalScore + score,
			RankedScore = RankedScore + rankedScore,
			PlayCount = PlayCount + 1
		};
	}

	public bool Equals(UserStats? other)
	{
		if (other is null) return false;
		return UserId == other.UserId && Mode == other.Mode;
	}

	public override bool Equals(object? obj)
	{
		return obj is UserStats other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(UserId, Mode);
	}
}