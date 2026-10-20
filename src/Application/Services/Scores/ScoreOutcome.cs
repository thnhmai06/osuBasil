using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Services.Scores;

/// <summary>The result of validating and recording a score submission.</summary>
internal readonly record struct ScoreOutcome(
	ScoreRejection? Rejection,
	ScoreRecord? Score,
	RoundScore? RoundScore,
	UserStats? Stats);