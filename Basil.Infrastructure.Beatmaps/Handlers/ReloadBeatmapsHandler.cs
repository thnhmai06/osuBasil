using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class ReloadBeatmapsHandler(Rulesets rulesets) : IRequestHandler<ReloadBeatmaps, RulesetUpdateResult>
{
	public ValueTask<RulesetUpdateResult> Handle(ReloadBeatmaps request, CancellationToken ct)
	{
		return ValueTask.FromResult(rulesets.ScopeTo(request.Actor).Reload());
	}
}