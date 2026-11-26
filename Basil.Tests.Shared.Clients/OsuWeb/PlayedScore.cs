using System.Globalization;

namespace Basil.Tests.Shared.Clients.OsuWeb;

/// <summary>A finished play a client submits: hit counts, combo and the grade they earn, kept consistent with each other.</summary>
/// <param name="BeatmapMd5">The md5 of the beatmap that was played.</param>
/// <param name="StoryboardMd5">The md5 of the beatmap's storyboard, or <see langword="null" /> when it has none.</param>
/// <param name="Mode">The game mode number.</param>
/// <param name="Mods">The mods as the client's flag number.</param>
/// <param name="Num300">The number of 300 hits.</param>
/// <param name="Num100">The number of 100 hits.</param>
/// <param name="Num50">The number of 50 hits.</param>
/// <param name="NumGeki">The number of gekis.</param>
/// <param name="NumKatu">The number of katus.</param>
/// <param name="NumMiss">The number of misses.</param>
/// <param name="TotalScore">The score.</param>
/// <param name="MaxCombo">The longest combo.</param>
/// <param name="FullCombo">Whether the play had no break.</param>
/// <param name="Grade">
///     The grade as the client writes it: <c>XH</c>, <c>X</c>, <c>SH</c>, <c>S</c>, <c>A</c>, <c>B</c>,
///     <c>C</c>, <c>D</c> or <c>F</c>.
/// </param>
/// <param name="Passed">Whether the play was completed.</param>
/// <param name="Timestamp">When the play ended, to the second.</param>
public sealed record PlayedScore(
	string BeatmapMd5,
	string? StoryboardMd5,
	int Mode,
	int Mods,
	int Num300,
	int Num100,
	int Num50,
	int NumGeki,
	int NumKatu,
	int NumMiss,
	int TotalScore,
	int MaxCombo,
	bool FullCombo,
	string Grade,
	bool Passed,
	DateTimeOffset Timestamp)
{
	private const int Hidden = 8;

	private const int Flashlight = 1024;

	/// <summary>Gets the number of objects the play covered.</summary>
	public int Objects => Num300 + Num100 + Num50 + NumMiss;

	/// <summary>Gets the accuracy as a ratio from 0 to 1.</summary>
	public double Accuracy => Objects == 0 ? 1 : (300.0 * Num300 + 100.0 * Num100 + 50.0 * Num50) / (300.0 * Objects);

	/// <summary>Gets the timestamp as the score line carries it.</summary>
	public string TimestampText => Timestamp.ToUniversalTime().ToString("yyMMddHHmmss", CultureInfo.InvariantCulture);

	/// <summary>Makes a play with random but consistent numbers.</summary>
	/// <param name="random">The source of the numbers; a seeded one makes the play repeatable.</param>
	/// <param name="beatmapMd5">The md5 of the beatmap that was played.</param>
	/// <param name="passed">Whether the play was completed; a failed play is graded <c>F</c> and has more misses.</param>
	/// <param name="mode">The game mode number.</param>
	/// <param name="mods">The mods as the client's flag number.</param>
	/// <param name="storyboardMd5">The md5 of the beatmap's storyboard, or <see langword="null" />.</param>
	/// <param name="at">When the play ended; the current time when omitted.</param>
	public static PlayedScore Generate(Random random, string beatmapMd5, bool passed = true, int mode = 0,
		int mods = 0, string? storyboardMd5 = null, DateTimeOffset? at = null)
	{
		var objects = random.Next(150, 700);
		var miss = passed ? random.Next(3) == 0 ? random.Next(1, 6) : 0 : random.Next(5, 40);
		var n50 = random.Next(0, 6);
		var n100 = random.Next(0, objects / 10);
		var n300 = Math.Max(0, objects - miss - n50 - n100);
		var maxCombo = miss == 0 ? objects : random.Next(objects / 4, objects);
		var geki = random.Next(0, n300 / 3 + 1);
		var katu = random.Next(0, n100 / 2 + 1);
		var total = (int)((300L * n300 + 100L * n100 + 50L * n50) * (1 + maxCombo / 25.0));
		var time = at ?? DateTimeOffset.UtcNow;
		return new PlayedScore(beatmapMd5, storyboardMd5, mode, mods, n300, n100, n50, geki, katu, miss, total,
			maxCombo, miss == 0, passed ? GradeOf(n300, n100, n50, miss, mods) : "F", passed,
			new DateTimeOffset(time.UtcDateTime.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond,
				TimeSpan.Zero));
	}

	/// <summary>Gives the grade the client shows for hit counts and mods.</summary>
	public static string GradeOf(int num300, int num100, int num50, int numMiss, int mods)
	{
		var total = num300 + num100 + num50 + numMiss;
		var ratio300 = total == 0 ? 1.0 : (double)num300 / total;
		var ratio50 = total == 0 ? 0.0 : (double)num50 / total;
		var bright = (mods & (Hidden | Flashlight)) != 0;
		return ratio300 switch
		{
			_ when num300 == total => bright ? "XH" : "X",
			> 0.9 when ratio50 <= 0.01 && numMiss == 0 => bright ? "SH" : "S",
			> 0.9 => "A",
			> 0.8 when numMiss == 0 => "A",
			> 0.8 => "B",
			> 0.7 when numMiss == 0 => "B",
			> 0.6 => "C",
			_ => "D"
		};
	}
}