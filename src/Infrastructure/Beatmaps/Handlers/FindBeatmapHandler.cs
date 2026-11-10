using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Beatmaps.Logic;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

/// <summary>Answers <see cref="FindBeatmap" /> from the stored beatmaps.</summary>
internal sealed class FindBeatmapHandler(IBeatmapRepository beatmaps) : IRequestHandler<FindBeatmap, BeatmapRecord?>
{
	/// <inheritdoc />
	public async ValueTask<BeatmapRecord?> Handle(FindBeatmap request, CancellationToken ct)
	{
		return await beatmaps.GetAsync(request.BeatmapId, ct);
	}
}