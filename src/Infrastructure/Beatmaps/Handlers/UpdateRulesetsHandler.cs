using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Services;
using Basil.Application.Common;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

/// <summary>Handles <see cref="UpdateRulesets" /> with the privileges of the sending actor.</summary>
internal sealed class UpdateRulesetsHandler(Rulesets rulesets)
	: IRequestHandler<UpdateRulesets, (Result<RulesetOutcome> Result, RulesetStageResult? Stage)>
{
	/// <inheritdoc />
	public async ValueTask<(Result<RulesetOutcome> Result, RulesetStageResult? Stage)> Handle(UpdateRulesets request,
		CancellationToken ct)
	{
		return await rulesets.ScopeTo(request.Actor).UpdateAsync(ct);
	}
}