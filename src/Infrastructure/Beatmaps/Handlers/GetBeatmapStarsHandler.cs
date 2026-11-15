using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Ports;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

/// <summary>Answers <see cref="GetBeatmapStars" /> with the calculated star rating, or nothing when it cannot be calculated.</summary>
internal sealed class GetBeatmapStarsHandler(IBeatmapCalculator calculator)
	: IRequestHandler<GetBeatmapStars, double?>
{
	/// <inheritdoc />
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
