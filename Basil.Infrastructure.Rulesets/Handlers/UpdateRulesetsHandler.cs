using Basil.Application.Rulesets.Messages;
using Basil.Application.Rulesets.Objects;
using Basil.Application.Rulesets.Services;
using Mediator;

namespace Basil.Infrastructure.Rulesets.Handlers;

internal sealed class UpdateRulesetsHandler(RulesetLibrary rulesets)
	: IRequestHandler<UpdateRulesets, (RulesetUpdateResult Result, RulesetStageResult? Stage)>
{
	public async ValueTask<(RulesetUpdateResult Result, RulesetStageResult? Stage)> Handle(UpdateRulesets request,
		CancellationToken ct)
	{
		return await rulesets.ScopeTo(request.Actor).UpdateAsync(ct);
	}
}