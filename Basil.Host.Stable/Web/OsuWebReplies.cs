namespace Basil.Host.Stable.Web;

internal static class OsuWebReplies
{
	public const string SubmissionRefused = "error: no";

	public const string BeatmapChartId = "beatmap";

	public const string BeatmapChartName = "Beatmap Ranking";

	public const string OverallChartId = "overall";

	public const string OverallChartName = "Overall Ranking";

	public const string NewAchievements = "achievements-new:";

	public const string NotRanked = "not ranked";

	public const int DirectPageSize = 100;

	public const int DirectMorePages = 101;

	public const int DirectAllModes = -1;

	public const int DirectAllStatuses = 4;

	public static readonly string[] DirectSortTabs = ["Newest", "Top+Rated", "Most+Played"];
}