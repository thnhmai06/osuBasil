using Basil.Application.Shared.Exceptions;
using Basil.Application.Shared.Paging;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps.Persistence;

public interface IBeatmapsetRepository
{
	ValueTask<BeatmapsetRecord?> GetAsync(int id, CancellationToken ct = default);

	Task UpdateAsync(BeatmapsetRecord set);

	Task<Page<BeatmapsetRecord>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken ct = default);

	Task<IReadOnlyList<int>> ListIdsAsync(CancellationToken ct = default);

	Task<IReadOnlyList<BeatmapsetRecord>> CommitAsync(BeatmapsetChanges changes);

	Task DeleteAsync(BeatmapsetRecord set);
}