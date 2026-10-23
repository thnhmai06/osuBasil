using Basil.Application.Persistence.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Domain.Common;

namespace Basil.Application.Capabilities.Beatmaps;

/// <summary>Finds the stored beatmapset a read archive belongs to and whether storing it would touch something locked.</summary>
internal sealed class BeatmapsetMatcher(IBeatmapRepository beatmaps, IBeatmapsetRepository beatmapsets)
{
	/// <summary>Finds the stored beatmapset a read beatmapset belongs to.</summary>
	/// <param name="content">What was read.</param>
	/// <param name="hashes">The MD5 hash of each difficulty's file.</param>
	/// <param name="folderId">
	///     The id of the stored folder the content lies in, or <see langword="null" /> when it lies in
	///     none.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The stored beatmapset the content belongs to, or <see langword="null" /> when it is a new one.</returns>
	/// <remarks>
	///     The beatmapset is the stored one that already has a difficulty of the read set; otherwise the stored one with
	///     the folder's id; otherwise the stored one with the id the read set declares.
	/// </remarks>
	public async Task<BeatmapsetRecord?> ResolveAsync(BeatmapsetContent content,
		IReadOnlyDictionary<BeatmapsetDifficulty, Md5> hashes, int? folderId, CancellationToken cancellationToken)
	{
		foreach (var hash in hashes.Values)
		{
			var beatmap = await beatmaps.GetAsync(hash, cancellationToken);
			if (beatmap is not null) return beatmap.Beatmapset;
		}

		if (folderId is { } id)
		{
			var inFolder = await beatmapsets.GetAsync(id, cancellationToken);
			if (inFolder is not null) return inFolder;
		}

		return content.OnlineSetId is > 0
			? await beatmapsets.GetAsync(content.OnlineSetId.Value, cancellationToken)
			: null;
	}

	/// <summary>Determines whether storing what an archive holds would change something locked.</summary>
	/// <param name="existing">The stored set the archive replaces, or <see langword="null" />.</param>
	/// <param name="content">What the archive holds.</param>
	/// <param name="hashes">The hash of each difficulty.</param>
	/// <param name="stored">The stored beatmap with each difficulty's hash, or <see langword="null" />.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     <see langword="true" /> when the set is locked, or a locked beatmap (or a beatmap of a locked set) would be
	///     taken over, changed or removed.
	/// </returns>
	public async Task<bool> TouchesLockedAsync(BeatmapsetRecord? existing, BeatmapsetContent content,
		Dictionary<BeatmapsetDifficulty, Md5> hashes, Dictionary<BeatmapsetDifficulty, BeatmapRecord?> stored,
		CancellationToken cancellationToken)
	{
		if (existing is { Locked: true }) return true;

		foreach (var difficulty in content.Difficulties)
		{
			var matched = stored[difficulty] ?? (difficulty.OnlineId is > 0
				? await beatmaps.GetAsync(difficulty.OnlineId.Value, cancellationToken)
				: null);
			if (matched is null) continue;
			if (!matched.Beatmapset.Equals(existing) && matched.IsLocked()) return true;
		}

		if (existing is null) return false;
		var kept = hashes.Values.ToHashSet();
		return (await beatmaps.ListAsync(existing, cancellationToken)).Any(beatmap =>
			beatmap.Locked && !kept.Contains(beatmap.Hash));
	}
}
