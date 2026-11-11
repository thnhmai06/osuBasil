using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Domain.Beatmaps;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

/// <summary>Answers <see cref="FindBeatmapByFileName" /> from the stored beatmaps.</summary>
internal sealed class FindBeatmapByFileNameHandler(IBeatmapRepository beatmaps)
	: IRequestHandler<FindBeatmapByFileName, BeatmapRecord?>
{
	/// <inheritdoc />
	public async ValueTask<BeatmapRecord?> Handle(FindBeatmapByFileName request, CancellationToken ct)
	{
		var page = await beatmaps.ListAsync(new BeatmapQuery(FileName: request.FileName), new PageRequest(0, 1), ct);
		return page.Items.FirstOrDefault();
	}
}