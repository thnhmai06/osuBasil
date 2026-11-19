using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Services;
using Basil.Application.Shared.Actors;
using Mediator;

namespace Basil.Application.Beatmaps.Messages;

public sealed record GetRulesetVersion(IActor Actor)
	: IRequest<(RulesetOutcome Result, RulesetVersions? Versions)>;

public sealed record UpdateRulesets(IActor Actor)
	: IRequest<(RulesetOutcome Result, RulesetStageResult? Stage)>;

public sealed record ReloadBeatmaps(IActor Actor) : IRequest<RulesetOutcome>;