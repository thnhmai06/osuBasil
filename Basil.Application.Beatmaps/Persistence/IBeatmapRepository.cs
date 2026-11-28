using Basil.Application.Shared.Paging;
using Basil.Domain.Beatmaps;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Beatmaps.Persistence;

public interface IBeatmapRepository
{
	Task UpdateAsync(BeatmapRecord beatmap);

	ValueTask<BeatmapRecord?> GetAsync(int id, CancellationToken ct = default);

	ValueTask<BeatmapRecord?> GetAsync(Md5Hash hash, CancellationToken ct = default);

	Task<IReadOnlyList<BeatmapRecord>> ListAsync(BeatmapsetRecord set, CancellationToken ct = default);

	/// <summary>Lists, by id, the beatmaps of all of <paramref name="sets"/>.</summary>
	Task<IReadOnlyList<BeatmapRecord>> ListAsync(IReadOnlyCollection<BeatmapsetRecord> sets,
		CancellationToken ct = default);

	Task<Page<BeatmapRecord>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken ct = default);

	/// <summary>Finds the beatmap of each .osu file name, in the order of <paramref name="fileNames"/>; a name that matches none gives <see langword="null"/>.</summary>
	Task<IReadOnlyList<BeatmapRecord?>> ListAsync(IReadOnlyList<string> fileNames, CancellationToken ct = default);

	/// <summary>Lists, by id, the beatmaps after <paramref name="afterId"/> whose difficulty is not calculated yet.</summary>
	Task<IReadOnlyList<BeatmapRecord>> ListUncalculatedAsync(int afterId, int limit,
		CancellationToken ct = default);

	Task RetainAsync(BeatmapsetRecord set, IReadOnlyCollection<BeatmapRecord> keep);
}