using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Persistence;

internal readonly record struct ScoreOutcome(
	SubmissionRejectionReason? Rejection,
	ScoreRecord? Score,
	UserStats? Stats,
	ScoreRecord? PreviousBest = null);