using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Ports;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class GetBeatmapStarsHandler(IBeatmapCalculator calculator)
	: IRequestHandler<GetBeatmapStars, double?>
{
	public async ValueTask<double?> Handle(GetBeatmapStars request, CancellationToken ct)
	{
		try
		{
			return await calculator.GetStarsAsync(request.Beatmap, request.Beatmap.Metadata.Difficulty.Mode,
				request.Mods, ct);
		}
		catch (Exception e) when (e is not OperationCanceledException)
		{
			return null;
		}
	}
}