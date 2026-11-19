using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Services;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class UpdateRulesetsHandler(Rulesets rulesets)
	: IRequestHandler<UpdateRulesets, (RulesetUpdateResult Result, RulesetStageResult? Stage)>
{
	public async ValueTask<(RulesetUpdateResult Result, RulesetStageResult? Stage)> Handle(UpdateRulesets request,
		CancellationToken ct)
	{
		return await rulesets.ScopeTo(request.Actor).UpdateAsync(ct);
	}
}