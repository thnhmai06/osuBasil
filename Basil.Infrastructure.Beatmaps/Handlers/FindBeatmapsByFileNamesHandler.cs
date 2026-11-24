using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Domain.Beatmaps;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class FindBeatmapsByFileNamesHandler(IBeatmapRepository beatmaps)
	: IRequestHandler<FindBeatmapsByFileNames, IReadOnlyList<BeatmapRecord?>>
{
	public async ValueTask<IReadOnlyList<BeatmapRecord?>> Handle(FindBeatmapsByFileNames request,
		CancellationToken ct)
	{
		return await beatmaps.ListAsync(request.FileNames, ct);
	}
}
