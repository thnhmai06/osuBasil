using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

/// <summary>Handles <see cref="ReloadBeatmaps" /> with the privileges of the sending actor.</summary>
internal sealed class ReloadBeatmapsHandler(Rulesets rulesets) : IRequestHandler<ReloadBeatmaps, RulesetOutcome>
{
	/// <inheritdoc />
	public ValueTask<RulesetOutcome> Handle(ReloadBeatmaps request, CancellationToken ct)
	{
		return ValueTask.FromResult(rulesets.ScopeTo(request.Actor).Reload());
	}
}