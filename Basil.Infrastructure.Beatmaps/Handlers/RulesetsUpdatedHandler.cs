using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Services;
using Basil.Application.Rulesets.Objects.Events;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Beatmaps.Handlers;

/// <summary>Takes in the beatmapsets that waited for the calculation stack and calculates what stored beatmaps lack.</summary>
internal sealed class RulesetsUpdatedHandler(
	Beatmapsets beatmapsets,
	IBeatmapReloader reloader,
	ILogger<RulesetsUpdatedHandler> logger) : INotificationHandler<RulesetsUpdated>
{
	public async ValueTask Handle(RulesetsUpdated notification, CancellationToken ct)
	{
		try
		{
			var result = await beatmapsets.ScanAsync(ct);
			logger.LogInformation("Scanned the beatmapset storage: {Imported} taken in, {Updated} updated", result.Imported,
				result.Updated);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			logger.LogError(exception, "Could not scan the beatmapset storage after the calculation stack was installed:");
		}

		reloader.TryStart(true);
	}
}
