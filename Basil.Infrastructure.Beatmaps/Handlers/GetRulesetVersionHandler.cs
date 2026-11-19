using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Services;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class GetRulesetVersionHandler(Rulesets rulesets)
	: IRequestHandler<GetRulesetVersion, (RulesetUpdateResult Result, RulesetVersions? Versions)>
{
	public ValueTask<(RulesetUpdateResult Result, RulesetVersions? Versions)> Handle(GetRulesetVersion request,
		CancellationToken ct)
	{
		return ValueTask.FromResult(rulesets.ScopeTo(request.Actor).Version());
	}
}