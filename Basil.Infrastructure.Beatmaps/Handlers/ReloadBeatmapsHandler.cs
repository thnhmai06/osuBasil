using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class ReloadBeatmapsHandler(Rulesets rulesets) : IRequestHandler<ReloadBeatmaps, RulesetOutcome>
{
	public ValueTask<RulesetOutcome> Handle(ReloadBeatmaps request, CancellationToken ct)
	{
		return ValueTask.FromResult(rulesets.ScopeTo(request.Actor).Reload());
	}
}