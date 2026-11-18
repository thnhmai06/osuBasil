using Basil.Application.Scores.Objects;
using Basil.Application.Shared.Actors;
using Mediator;

namespace Basil.Application.Scores.Messages;

public sealed record RecalculateScorePerformance(IActor Actor, ScorePerformanceScope Scope)
	: IRequest<ScorePerformanceOutcome>;
