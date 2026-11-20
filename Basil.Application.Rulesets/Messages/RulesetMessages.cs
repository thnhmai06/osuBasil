using Basil.Application.Rulesets.Objects;
using Basil.Application.Rulesets.Services;
using Basil.Application.Shared.Actors;
using Mediator;

namespace Basil.Application.Rulesets.Messages;

public sealed record GetRulesetVersion(IActor Actor)
	: IRequest<(RulesetUpdateResult Result, RulesetVersions? Versions)>;

public sealed record UpdateRulesets(IActor Actor)
	: IRequest<(RulesetUpdateResult Result, RulesetStageResult? Stage)>;