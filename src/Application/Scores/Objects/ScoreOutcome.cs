using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Objects;

/// <summary>The result of validating and recording a score submission.</summary>
internal readonly record struct ScoreOutcome(
	ScoreRejectionReason? Rejection,
	ScoreRecord? Score,
	UserStats? Stats);