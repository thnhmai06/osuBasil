using Basil.Application.Scores.Objects;
using Basil.Application.Scores.Persistence;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Game.Scores;
using Mediator;

namespace Basil.Application.Scores.Messages;

public sealed record SubmitScore(
	StableSession Actor,
	Submission Submission,
	BeatmapChecksums? Beatmap,
	SubmittedClient Client,
	Stream? Replay)
	: IRequest<(ScoreSubmissionOutcome Result, SubmissionRejectionReason? Rejection, ScoreReceipt? Receipt)>;