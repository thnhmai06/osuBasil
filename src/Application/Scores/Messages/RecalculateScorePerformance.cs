using Basil.Application.Scores.Objects;
using Basil.Application.Shared.Actors;
using Mediator;

namespace Basil.Application.Scores.Messages;

/// <summary>Asks Scores to start calculating the performance points of stored scores on behalf of an actor.</summary>
/// <param name="Actor">The actor acting.</param>
/// <param name="Scope">Which scores the calculation covers.</param>
public sealed record RecalculateScorePerformance(IActor Actor, ScorePerformanceScope Scope)
	: IRequest<ScorePerformanceOutcome>;
