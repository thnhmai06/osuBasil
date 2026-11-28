using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Rulesets.Objects;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class ReloadBeatmapsHandler(BeatmapReloads reloads) : IRequestHandler<ReloadBeatmaps, RulesetUpdateResult>
{
	public ValueTask<RulesetUpdateResult> Handle(ReloadBeatmaps request, CancellationToken ct)
	{
		return ValueTask.FromResult(reloads.ScopeTo(request.Actor).Reload());
	}
}