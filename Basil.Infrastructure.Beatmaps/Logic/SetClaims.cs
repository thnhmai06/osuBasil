using Basil.Application.Beatmaps.Persistence;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Infrastructure.Beatmaps.Logic;

internal sealed class SetClaims
{
	private readonly HashSet<Md5Hash> _beatmaps = [];
	private readonly HashSet<int> _sets = [];

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