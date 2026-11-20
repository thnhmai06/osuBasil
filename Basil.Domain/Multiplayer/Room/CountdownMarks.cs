namespace Basil.Domain.Multiplayer.Room;

/// <summary>Chooses the moments at which a room countdown is announced.</summary>
public static class CountdownMarks
{
	/// <summary>Gets the announcement moments of a countdown, as the time remaining, longest first.</summary>
	/// <param name="length">The total length of the countdown; only moments shorter than it are returned.</param>
	/// <param name="startsRound">Whether the countdown ends by starting a round; such a countdown also announces each of the last four seconds.</param>
	/// <returns>The remaining times at which to announce the countdown; empty when the countdown is too short for any.</returns>
	public static IReadOnlyList<TimeSpan> For(TimeSpan length, bool startsRound)
	{
		int[] seconds = startsRound ? [60, 30, 10, 5, 4, 3, 2, 1] : [60, 30, 10, 5];
		return [.. seconds.Select(second => TimeSpan.FromSeconds(second)).Where(mark => mark < length)];
	}
}