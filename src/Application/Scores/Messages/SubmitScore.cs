using Basil.Application.Scores.Objects;
using Basil.Application.Scores.Persistence;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Scores;
using Mediator;

namespace Basil.Application.Scores.Messages;

public sealed record SubmitScore(
	StableConnection Actor,
	Submission Submission,
	BeatmapChecksums? Beatmap,
	SubmittedClient Client,
	Stream? Replay)
	: IRequest<(ScoreSubmissionOutcome Result, SubmissionRejectionReason? Rejection, ScoreReceipt? Receipt)>;