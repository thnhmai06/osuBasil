using Basil.Application.Services.Contracts.Beatmaps;
using Basil.Infrastructure.Runtime.Events;

namespace Basil.Infrastructure.Runtime.Beatmaps;

/// <summary>Discards the files derived from a beatmapset when it is imported again or deleted.</summary>
internal sealed class BeatmapAssetsHandler(IBeatmapAssets assets) : IEventHandler<BeatmapsetEvent>
{
	public async ValueTask HandleAsync(BeatmapsetEvent @event, CancellationToken cancellationToken)
	{
		var set = @event switch
		{
			BeatmapsetImported imported => imported.Set,
			BeatmapsetDeleted deleted => deleted.Set,
			_ => null
		};
		if (set is not null) await assets.ForgetAsync(set, cancellationToken);
	}
}
