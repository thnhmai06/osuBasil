using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Beatmaps.Logic;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

/// <summary>Answers <see cref="GetBeatmap" /> for what the caller may see.</summary>
internal sealed class GetBeatmapHandler(IBeatmapRepository beatmaps, TimeProvider clock)
	: IRequestHandler<GetBeatmap, BeatmapRecord?>
{
	/// <inheritdoc />
	public async ValueTask<BeatmapRecord?> Handle(GetBeatmap request, CancellationToken ct)
	{
		return await beatmaps.GetAsync(request.BeatmapId, ct) is { } map &&
		       BeatmapVisibility.CanView(map, BeatmapVisibility.MayViewHidden(request.Actor, clock))
			? map
			: null;
	}
}