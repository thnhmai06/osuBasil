using System.Globalization;
using System.Text;
using Basil.Protocol.Game.Stable.Models.Beatmaps;
using Basil.Protocol.Game.Stable.Models.Web;

namespace Basil.Protocol.Game.Stable.Wire.Web;

/// <summary>Writes the delimited text the osu! client reads from the web endpoints.</summary>
public static class OsuWebWriter
{
	/// <summary>The leaderboard answer for a beatmap the server does not know.</summary>
	public const string LeaderboardNotFound = "-1|false";

	private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

	/// <summary>Writes a leaderboard.</summary>
	public static string Leaderboard(LeaderboardReply reply)
	{
		var lines = new List<string>
		{
			string.Create(CultureInfo.InvariantCulture,
				$"{reply.RankedStatus}|false|{reply.BeatmapId}|{reply.BeatmapsetId}|{reply.Rows.Count}|0"),
			"0", reply.BeatmapName, "10.0", string.Empty
		};
		lines.AddRange(reply.Rows.Select(row => string.Create(CultureInfo.InvariantCulture,
			$"{row.ScoreId}|{row.PlayerName}|{row.TotalScore}|{row.MaxCombo}|{row.Num50}|{row.Num100}|{row.Num300}|{row.NumMiss}|{row.NumKatu}|{row.NumGeki}|{(row.FullCombo ? 1 : 0)}|{row.Mods}|{row.PlayerId}|{row.Rank}|{row.UnixTime}|1")));
		return string.Join('\n', lines);
	}

	/// <summary>Writes the friend list: one user id per line.</summary>
	public static string Friends(IEnumerable<int> userIds)
	{
		return string.Join('\n', userIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
	}

	/// <summary>Writes the answer to a beatmap info request: one line per known beatmap.</summary>
	public static string BeatmapInfo(IEnumerable<BeatmapInfoResponse> beatmaps)
	{
		return string.Join('\n', beatmaps.Select(map => string.Create(CultureInfo.InvariantCulture,
			$"{map.Index}|{map.MapId}|{map.MapSetId}|{map.ThreadId}|{map.RankedStatus}|{Grade(map.GradeOsu)}|{Grade(map.GradeCatch)}|{Grade(map.GradeTaiko)}|{Grade(map.GradeMania)}|{map.Checksum}")));
	}

	/// <summary>Writes an osu!direct result list.</summary>
	/// <param name="header">The first line: the number of results, or the page-full marker.</param>
	/// <param name="sets">The beatmapsets.</param>
	public static string DirectList(int header, IEnumerable<DirectSet> sets)
	{
		return string.Join('\n',
			sets.Select(DirectLine).Prepend(header.ToString(CultureInfo.InvariantCulture)));
	}

	/// <summary>Writes one beatmapset line.</summary>
	public static string DirectLine(DirectSet set)
	{
		var line = string.Create(CultureInfo.InvariantCulture,
			$"{set.Id}.osz|{DirectText(set.Artist)}|{DirectText(set.Title)}|{DirectText(set.Creator)}|{set.RankedStatus}|10.0|{set.UpdatedAt.ToString(DateFormat, CultureInfo.InvariantCulture)}|{set.Id}|0|{(set.HasVideo ? 1 : 0)}|0|0|0");
		return set.Difficulties is null
			? line
			: line + "|" + string.Join(',', set.Difficulties.Select(Difficulty));
	}

	/// <summary>Writes the charts of an accepted score submission.</summary>
	public static string Submission(SubmissionReply reply)
	{
		var parts = new List<string>
		{
			string.Create(CultureInfo.InvariantCulture, $"beatmapId:{reply.BeatmapId}"),
			string.Create(CultureInfo.InvariantCulture, $"beatmapSetId:{reply.BeatmapsetId}"),
			"beatmapPlaycount:0",
			"beatmapPasscount:0",
			$"approvedDate:{reply.ApprovedAt?.UtcDateTime.ToString(DateFormat, CultureInfo.InvariantCulture)}",
			"\n"
		};
		AddChart(parts, reply.Beatmap);
		parts.Add("\n");
		AddChart(parts, reply.Overall);
		parts.Add("achievements-new:");
		return string.Join('|', parts);
	}

	private static void AddChart(List<string> parts, SubmissionChart chart)
	{
		parts.Add($"chartId:{chart.Id}");
		parts.Add($"chartName:{chart.Name}");
		foreach (var entry in chart.Entries)
			parts.Add($"{entry.Name}Before:{Value(entry.Before, entry.Kind)}|{entry.Name}After:{Value(entry.After, entry.Kind)}");
		if (chart.OnlineScoreId is { } id) parts.Add(string.Create(CultureInfo.InvariantCulture, $"onlineScoreId:{id}"));
	}

	private static string Value(double? value, ChartValueKind kind)
	{
		if (value is not { } number) return string.Empty;
		if (kind is ChartValueKind.Whole)
			return number == 0 ? string.Empty : ((long)number).ToString(CultureInfo.InvariantCulture);
		return Math.Round(number, 2) is var rounded and not 0
			? rounded.ToString("0.0#", CultureInfo.InvariantCulture)
			: string.Empty;
	}

	private static string Grade(sbyte grade)
	{
		return grade < 0 ? "N" : grade.ToString(CultureInfo.InvariantCulture);
	}

	private static string Difficulty(DirectDifficulty difficulty)
	{
		return string.Create(CultureInfo.InvariantCulture,
			$"[{difficulty.Stars:0.00}⭐] {DirectText(difficulty.Name)} {{cs: {Decimal(difficulty.CircleSize)} / od: {Decimal(difficulty.OverallDifficulty)} / ar: {Decimal(difficulty.ApproachRate)} / hp: {Decimal(difficulty.HealthDrain)}}}@{difficulty.Mode}");
	}

	// The client splits the line at '|', so a text cannot carry one; bancho.py writes 'I' instead.
	private static string DirectText(string value)
	{
		return value.Replace('|', 'I');
	}

	// The client shows a stat with a decimal point even when it is whole: 4.0, not 4.
	private static string Decimal(double value)
	{
		var text = value.ToString("R", CultureInfo.InvariantCulture);
		return text.Contains('.') ? text : text + ".0";
	}
}
