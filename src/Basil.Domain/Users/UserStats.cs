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
/// <param name="UserId">The identifier of the user these statistics belong to.</param>
/// <param name="Mode">The game mode these statistics are for.</param>
/// <param name="TotalScore">The cumulative score across every submitted play.</param>
/// <param name="RankedScore">The cumulative score across every submitted play on ranked beatmaps.</param>
/// <param name="PlayCount">The number of plays submitted.</param>
public sealed record UserStats(int UserId, GameMode Mode, long TotalScore, long RankedScore, int PlayCount)
{
	/// <summary>Gets the fixed accuracy value Basil reports to clients, in place of a real calculation.</summary>
	public double Accuracy => 100.0;

	/// <summary>Gets the fixed performance-points value Basil reports to clients, in place of a real calculation.</summary>
	public int Pp => 727;

	/// <summary>Gets the fixed leaderboard rank Basil reports to clients, in place of a real calculation.</summary>
	public int Rank => UserId;

	/// <summary>Gets an empty statistics record for a user who has not submitted a score yet.</summary>
	/// <param name="userId">The identifier of the user.</param>
	/// <param name="mode">The game mode.</param>
	/// <returns>A statistics record with every cumulative value at zero.</returns>
	public static UserStats Empty(int userId, GameMode mode)
	{
		return new UserStats(userId, mode, 0, 0, 0);
	}

	/// <summary>
	///     Returns a copy of these statistics with a submitted score's totals added and the play
	///     count incremented.
	/// </summary>
	/// <param name="totalScore">The score to add to <see cref="TotalScore" />.</param>
	/// <param name="rankedScore">The score to add to <see cref="RankedScore" />.</param>
	/// <returns>A new statistics record reflecting the submitted score.</returns>
	public UserStats WithSubmittedScore(long totalScore, long rankedScore)
	{
		return this with
		{
			TotalScore = TotalScore + totalScore,
			RankedScore = RankedScore + rankedScore,
			PlayCount = PlayCount + 1
		};
	}
}
