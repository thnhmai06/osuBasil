using Basil.Application.Beatmaps.Messages;
using Basil.Domain.Cryptography;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Mediator;

namespace Basil.Host.Shared.Views;

/// <summary>A score as the API shows it.</summary>
/// <remarks>
///     <c>Pp</c> is <see langword="null" /> until it is calculated; <c>PpPending</c> says whether it is still expected
///     (a failed score, or a score on a beatmap the server does not have, never has pp).
/// </remarks>
public sealed record ScoreView(
	int Id,
	int? UserId,
	string? BeatmapHash,
	GameMode Mode,
	string Mods,
	int TotalScore,
	ushort MaxCombo,
	double Accuracy,
	Grade Grade,
	bool Passed,
	bool FullCombo,
	DateTimeOffset Timestamp,
	double? Pp,
	bool PpPending)
{
	/// <summary>Describes a score.</summary>
	/// <param name="score">The score.</param>
	/// <param name="beatmapKnown">Whether the server has the beatmap the score was set on.</param>
	public static ScoreView Of(ScoreRecord score, bool beatmapKnown)
	{
		var v = score.Value;
		return new ScoreView(score.Id, v.UserId, v.BeatmapHash?.HashValue, v.Mode, v.Mods.ToString(),
			v.LastState.TotalScore,
			v.LastState.MaxCombo, v.Accuracy, v.Grade, v.IsPassed, v.LastState.IsFullCombo, v.Timestamp,
			v.Pp, beatmapKnown && v.IsPassed && v.Pp is null);
	}

	/// <summary>Describes scores, finding out which of their beatmaps the server has.</summary>
	/// <param name="sender">Where the beatmaps are asked from.</param>
	/// <param name="scores">The scores.</param>
	/// <param name="ct">A token that cancels the lookup.</param>
	/// <returns>The views, in the order of <paramref name="scores" />.</returns>
	public static async Task<IReadOnlyList<ScoreView>> DescribeAsync(ISender sender, IEnumerable<ScoreRecord> scores,
		CancellationToken ct)
	{
		var list = scores.ToList();
		var known = new Dictionary<Md5, bool>();
		foreach (var hash in list.Select(score => score.Value.BeatmapHash).OfType<Md5>().Distinct())
			known[hash] = await sender.Send(new FindBeatmapByHash(hash), ct) is not null;

		return [.. list.Select(score => Of(score, score.Value.BeatmapHash is { } hash && known[hash]))];
	}
}