using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Capabilities;

/// <summary>The result of validating and recording a score submission.</summary>
internal readonly record struct ScoreOutcome(
	ScoreRejection? Rejection,
	ScoreRecord? Score,
	UserStats? Stats);