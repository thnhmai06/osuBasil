using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Domain.Beatmaps;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

/// <summary>Answers <see cref="FindBeatmapByHash" /> from the stored beatmaps.</summary>
internal sealed class FindBeatmapByHashHandler(IBeatmapRepository beatmaps)
	: IRequestHandler<FindBeatmapByHash, BeatmapRecord?>
{
	/// <inheritdoc />
	public async ValueTask<BeatmapRecord?> Handle(FindBeatmapByHash request, CancellationToken ct)
	{
		return await beatmaps.GetAsync(request.Hash, ct);
	}
}