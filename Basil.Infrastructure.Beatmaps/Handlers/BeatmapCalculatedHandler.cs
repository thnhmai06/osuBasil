using Basil.Application.Beatmaps.Objects;
using Basil.Application.Rulesets.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class BeatmapCalculatedHandler(Beatmapsets beatmapsets) : INotificationHandler<BeatmapCalculated>
{
	public async ValueTask Handle(BeatmapCalculated notification, CancellationToken ct)
	{
		await beatmapsets.CalculatedAsync(notification.BeatmapId, notification.Analysis);
	}
}