using Basil.Application.Rulesets.Messages;
using Basil.Application.Rulesets.Ports;
using Mediator;

namespace Basil.Infrastructure.Rulesets.Handlers;

internal sealed class GetBeatmapStarsHandler(IBeatmapCalculator calculator)
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
			return null;
		}
	}
}