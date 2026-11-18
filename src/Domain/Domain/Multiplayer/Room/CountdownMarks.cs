namespace Basil.Domain.Multiplayer;

public static class CountdownMarks
{
	public static IReadOnlyList<TimeSpan> For(TimeSpan length, bool startsRound)
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
