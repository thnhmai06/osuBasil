using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Services;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class GetRulesetVersionHandler(Rulesets rulesets)
	: IRequestHandler<GetRulesetVersion, (RulesetOutcome Result, RulesetVersions? Versions)>
{
	public ValueTask<(RulesetOutcome Result, RulesetVersions? Versions)> Handle(GetRulesetVersion request,
		CancellationToken ct)
	{
		return ValueTask.FromResult(rulesets.ScopeTo(request.Actor).Version());
	}
}