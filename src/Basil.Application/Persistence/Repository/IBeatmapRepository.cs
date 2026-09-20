namespace Basil.Application.Persistence.Repository;

public interface IBeatmapRepository<TBeatmap> : IRecordRepository<TBeatmap> where TBeatmap : notnull
{
	Task<IReadOnlyCollection<TBeatmap>> SyncMetadataAsync(
		IReadOnlySet<TBeatmap> beatmaps,
		CancellationToken cancellationToken = default);
}