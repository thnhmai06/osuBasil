using Basil.Application.Rulesets.Messages;
using Basil.Application.Rulesets.Ports;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Rulesets.Handlers;

internal sealed class GetBeatmapStarsHandler(IBeatmapCalculator calculator, ILogger<GetBeatmapStarsHandler> logger)
	: IRequestHandler<GetBeatmapStars, double?>
{
	public async ValueTask<double?> Handle(GetBeatmapStars request, CancellationToken ct)
	{
		try
		{
			return await calculator.GetStarsAsync(request.BeatmapId, request.Checksum, request.Mode, request.Mods, ct);
		}
		catch (Exception e) when (e is not OperationCanceledException)
		{
			logger.LogWarning(e, "Could not calculate the stars of beatmap {BeatmapId}", request.BeatmapId);
			return null;
		}
	}
}