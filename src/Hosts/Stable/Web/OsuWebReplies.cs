namespace Basil.Host.Stable.Web;

/// <summary>The fixed answers and limits of the web endpoints of the osu! client, as the client expects them.</summary>
internal static class OsuWebReplies
{
	/// <summary>The answer to a score submission that is refused or that does not count (a failed play).</summary>
	public const string SubmissionRefused = "error: no";

	/// <summary>The id of the chart that compares the player's play with their previous best on the beatmap.</summary>
	public const string BeatmapChartId = "beatmap";

	/// <summary>The title of the beatmap chart.</summary>
	public const string BeatmapChartName = "Beatmap Ranking";

	/// <summary>The id of the chart that shows the effect of the play on the player's totals.</summary>
	public const string OverallChartId = "overall";

	/// <summary>The title of the overall chart.</summary>
	public const string OverallChartName = "Overall Ranking";

	/// <summary>The last field of the submission answer: the achievements unlocked, which Basil has none of.</summary>
	public const string NewAchievements = "achievements-new:";

	/// <summary>The answer to a rating request: Basil keeps no ratings, and only ranked beatmaps can be rated.</summary>
	public const string NotRanked = "not ranked";

	/// <summary>How many beatmapsets one page of osu!direct results holds.</summary>
	public const int DirectPageSize = 100;

	/// <summary>The result count reported for a full page, which tells the client that another page follows.</summary>
	public const int DirectMorePages = 101;

	/// <summary>The search texts of the osu!direct sort tabs; they filter nothing.</summary>
	public static readonly string[] DirectSortTabs = ["Newest", "Top+Rated", "Most+Played"];

	/// <summary>The <c>m</c> value that asks for every game mode.</summary>
	public const int DirectAllModes = -1;

	/// <summary>The <c>r</c> value that asks for every ranked status.</summary>
	public const int DirectAllStatuses = 4;
}
