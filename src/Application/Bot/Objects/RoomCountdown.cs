using Basil.Application.Users.Objects.Sessions;

namespace Basil.Application.Bot.Objects;

internal sealed record RoomCountdown(Session Actor, TimeSpan Length, bool StartsRound, DateTimeOffset StartedAt)
{
	public static readonly TimeSpan Min = TimeSpan.FromSeconds(1);

	public static readonly TimeSpan Max = TimeSpan.FromHours(1);

	public DateTimeOffset EndsAt => StartedAt + Length;

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