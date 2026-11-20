using Basil.Application.Beatmaps.Services;
using Basil.Application.Rulesets.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class RulesetsUpdatedHandler(IBeatmapReloader reloader) : INotificationHandler<RulesetsUpdated>
{
	public ValueTask Handle(RulesetsUpdated notification, CancellationToken ct)
	{
		reloader.TryStart(false);
		return ValueTask.CompletedTask;
	}
}