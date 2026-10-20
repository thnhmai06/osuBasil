using Basil.Application.Storage.Beatmaps;
using Basil.Domain.Common;

namespace Basil.Application.Services.Beatmaps;

/// <summary>The beatmapsets and difficulties already given to an archive by one scan or import.</summary>
/// <remarks>Two archives of one scan never become the same beatmapset nor share a difficulty: the first wins.</remarks>
internal sealed class SetClaims
{
	private readonly HashSet<Md5> _beatmaps = [];
	private readonly HashSet<int> _sets = [];

	/// <summary>Gives a beatmapset and its difficulties to one archive.</summary>
	/// <param name="change">The beatmapset to store.</param>
	/// <returns>
	///     <see langword="false" /> when an earlier archive already has the beatmapset or one of its difficulties, or
	///     the archive repeats a difficulty; nothing is claimed then.
	/// </returns>
	public bool TryClaim(BeatmapsetChange change)
	{
		var id = change.Existing?.Id ?? change.Data.OnlineId;
		var hashes = change.Data.Beatmaps.Select(beatmap => beatmap.Hash).ToList();
		if ((id is { } taken && _sets.Contains(taken)) || hashes.Count != hashes.Distinct().Count() ||
		    hashes.Any(_beatmaps.Contains))
			return false;

		if (id is { } claimed) _sets.Add(claimed);
		_beatmaps.UnionWith(hashes);
		return true;
	}
}
