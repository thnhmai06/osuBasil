using Basil.Application.Users.Objects.Connections;

namespace Basil.Application.Bot.Objects;

/// <summary>A countdown BasilBot runs in a room, identified by reference, that may start a round when it elapses.</summary>
/// <param name="Actor">The connection that started the countdown; it starts the round when the countdown elapses.</param>
/// <param name="Length">How long the countdown runs.</param>
/// <param name="StartsRound">Whether the countdown starts a round when it elapses.</param>
/// <param name="StartedAt">When the countdown started.</param>
internal sealed record RoomCountdown(Connection Actor, TimeSpan Length, bool StartsRound, DateTimeOffset StartedAt)
{
	/// <summary>The shortest countdown a room accepts.</summary>
	public static readonly TimeSpan Min = TimeSpan.FromSeconds(1);

	/// <summary>The longest countdown a room accepts.</summary>
	public static readonly TimeSpan Max = TimeSpan.FromHours(1);

	/// <summary>Gets when the countdown elapses.</summary>
	public DateTimeOffset EndsAt => StartedAt + Length;

	/// <summary>Gets the countdown marks in descending order.</summary>
	/// <param name="length">How long the countdown runs.</param>
	/// <param name="startsRound">Whether the countdown starts a round when it elapses.</param>
	/// <returns>The remaining times announced during the countdown.</returns>
	public static IReadOnlyList<TimeSpan> MarksFor(TimeSpan length, bool startsRound)
	{
		int[] seconds = startsRound ? [30, 10, 5, 4, 3] : [60, 30, 10, 5];
		var marks = seconds.Select(second => TimeSpan.FromSeconds(second)).Where(mark => mark < length).ToList();
		if (!startsRound) return [.. marks.OrderByDescending(mark => mark)];

		for (var minute = TimeSpan.FromMinutes(1); minute < length; minute += TimeSpan.FromMinutes(1))
			if (length - minute > TimeSpan.FromSeconds(5))
				marks.Add(minute);

		return [.. marks.OrderByDescending(mark => mark)];
	}
}
