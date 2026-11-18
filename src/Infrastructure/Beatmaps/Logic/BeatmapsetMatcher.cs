using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Ports;
using Basil.Domain.Beatmaps;
using Basil.Domain.Cryptography;

namespace Basil.Infrastructure.Beatmaps.Logic;

internal sealed class BeatmapsetMatcher(IBeatmapRepository beatmaps, IBeatmapsetRepository beatmapsets)
{
	public async Task<BeatmapsetRecord?> ResolveAsync(BeatmapsetContent content,
		IReadOnlyDictionary<BeatmapsetDifficulty, Md5> hashes, int? folderId, CancellationToken ct)
	{
		foreach (var hash in hashes.Values)
		{
			var beatmap = await beatmaps.GetAsync(hash, ct);
			if (beatmap is not null) return beatmap.Beatmapset;
		}

		if (folderId is { } id)
		{
			var inFolder = await beatmapsets.GetAsync(id, ct);
			if (inFolder is not null) return inFolder;
		}

		return content.OnlineSetId is > 0
			? await beatmapsets.GetAsync(content.OnlineSetId.Value, ct)
			: null;
	}

	public async Task<bool> TouchesLockedAsync(BeatmapsetRecord? existing, BeatmapsetContent content,
		Dictionary<BeatmapsetDifficulty, Md5> hashes, Dictionary<BeatmapsetDifficulty, BeatmapRecord?> stored,
		CancellationToken ct)
	{
		if (existing is { Locked: true }) return true;

		foreach (var difficulty in content.Difficulties)
		{
			var matched = stored[difficulty] ?? (difficulty.OnlineId is > 0
				? await beatmaps.GetAsync(difficulty.OnlineId.Value, ct)
				: null);
			if (matched is null) continue;
			if (!matched.Beatmapset.Equals(existing) && matched.IsLocked()) return true;
		}

		if (existing is null) return false;
		var kept = hashes.Values.ToHashSet();
		return (await beatmaps.ListAsync(existing, ct)).Any(beatmap =>
			beatmap.Locked && !kept.Contains(beatmap.Hash));
	}
}