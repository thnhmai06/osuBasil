using Basil.Application.Rulesets.Messages;
using Basil.Application.Rulesets.Objects;
using Basil.Application.Rulesets.Services;
using Mediator;

namespace Basil.Infrastructure.Rulesets.Handlers;

internal sealed class GetRulesetVersionHandler(RulesetLibrary rulesets)
	: IRequestHandler<GetRulesetVersion, (RulesetUpdateResult Result, RulesetVersions? Versions)>
{
	public ValueTask<(RulesetUpdateResult Result, RulesetVersions? Versions)> Handle(GetRulesetVersion request,
		CancellationToken ct)
	{
		return ValueTask.FromResult(rulesets.ScopeTo(request.Actor).Version());
	}
}