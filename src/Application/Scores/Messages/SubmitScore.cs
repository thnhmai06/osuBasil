using Basil.Application.Common;
using Basil.Application.Scores.Objects;
using Basil.Application.Scores.Persistence;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Scores;
using Mediator;

namespace Basil.Application.Scores.Messages;

/// <summary>Asks Scores to validate and record the score a game client submitted.</summary>
/// <param name="Actor">The game client that submits.</param>
/// <param name="Submission">The parsed submission.</param>
/// <param name="Beatmap">The beatmap the server knows for the submission, or <see langword="null" /> when it has none.</param>
/// <param name="Client">The osu! client information sent with the submission.</param>
/// <param name="Replay">The seekable replay stream at its start, left open by the answer, or <see langword="null" /> for a failed play.</param>
/// <remarks>The answer is ok when the score was stored; otherwise rejected with the reason, or denied.</remarks>
public sealed record SubmitScore(
	StableConnection Actor,
	Submission Submission,
	BeatmapChecksums? Beatmap,
	SubmittedClient Client,
	Stream? Replay)
	: IRequest<(Result<ScoreSubmissionOutcome> Result, ScoreRejectionReason? Rejection)>;
