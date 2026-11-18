using Basil.Application.Shared.Exceptions;
using Basil.Application.Shared.Paging;
using Basil.Domain.Beatmaps;


namespace Basil.Application.Beatmaps.Persistence;

public interface IBeatmapRepository
{
	Task UpdateAsync(BeatmapRecord beatmap);

	ValueTask<BeatmapRecord?> GetAsync(int id, CancellationToken ct = default);

	ValueTask<BeatmapRecord?> GetAsync(Md5 hash, CancellationToken ct = default);

	Task<IReadOnlyList<BeatmapRecord>> ListAsync(BeatmapsetRecord set, CancellationToken ct = default);

	Task<Page<BeatmapRecord>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken ct = default);

	Task RetainAsync(BeatmapsetRecord set, IReadOnlyCollection<BeatmapRecord> keep);
}