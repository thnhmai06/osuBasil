using Basil.Application.Rulesets.Objects;
using Basil.Application.Shared.Actors;
using Mediator;

namespace Basil.Application.Beatmaps.Messages;

public sealed record ReloadBeatmaps(IActor Actor) : IRequest<RulesetUpdateResult>;