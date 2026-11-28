namespace Basil.Protocol.Game.Stable.Models.Web;

/// <summary>The answer to an accepted score submission: the charts the client shows after a play.</summary>
/// <param name="BeatmapId">The beatmap id, or 0 when the server does not have the beatmap.</param>
/// <param name="BeatmapsetId">The beatmapset id, or 0.</param>
/// <param name="ApprovedAt">When the beatmapset was last approved, or <see langword="null"/>.</param>
/// <param name="Beatmap">The chart of the beatmap.</param>
/// <param name="Overall">The chart of the player's overall standing.</param>
public sealed record SubmissionReply(
	int BeatmapId,
	int BeatmapsetId,
	DateTimeOffset? ApprovedAt,
	SubmissionChart Beatmap,
	SubmissionChart Overall);

/// <summary>One chart of a <see cref="SubmissionReply"/>.</summary>
/// <param name="Id">The chart id the client keys it by.</param>
/// <param name="Name">The title the client shows.</param>
/// <param name="Entries">The rows, each with a before and an after value.</param>
/// <param name="OnlineScoreId">The id of the stored score, or <see langword="null"/> when the chart carries none.</param>
public sealed record SubmissionChart(
	string Id,
	string Name,
	IReadOnlyList<ChartEntry> Entries,
	long? OnlineScoreId);

/// <summary>One row of a chart; a missing or zero value is left empty on the wire.</summary>
/// <param name="Name">The row name, such as <c>rankedScore</c>.</param>
/// <param name="Before">The value before the play.</param>
/// <param name="After">The value after the play.</param>
/// <param name="Kind">How the values are written.</param>
public sealed record ChartEntry(string Name, double? Before, double? After, ChartValueKind Kind = ChartValueKind.Whole);

/// <summary>How a chart value is written.</summary>
public enum ChartValueKind
{
	/// <summary>A whole number.</summary>
	Whole,

	/// <summary>A number with up to two decimals, at least one.</summary>
	Fixed
}
