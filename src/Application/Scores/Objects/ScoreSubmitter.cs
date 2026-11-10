using Basil.Application.Common.Events;
using Basil.Application.Scores.Capabilities;
using Basil.Application.Scores.Objects.Events;
using Basil.Application.Scores.Persistence;
using Basil.Application.Scores.Ports;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Scores;

namespace Basil.Application.Scores.Objects;

/// <summary>
///     Takes the scores that osu! clients submit: validates and stores them, then publishes
///     <see cref="ScoreSubmitted" />.
/// </summary>
public sealed class ScoreSubmitter
{
	private readonly TimeProvider _clock;

	/// <summary>Creates the score submitter with the ports its work needs.</summary>
	/// <param name="recorder">Validates and stores a score.</param>
	/// <param name="events">The publisher for score events.</param>
	/// <param name="acceptance">Decides about scores on beatmaps the server does not have.</param>
	/// <param name="clock">The clock the privileges of a scope are evaluated at.</param>
	internal ScoreSubmitter(IScoreRecorder recorder, IEventPublisher<ScoreEvent> events,
		IScoreBeatmapAcceptance acceptance, TimeProvider clock)
	{
		Recorder = recorder;
		Events = events;
		Acceptance = acceptance;
		_clock = clock;
	}

	internal IScoreRecorder Recorder { get; }

	internal IEventPublisher<ScoreEvent> Events { get; }

	internal IScoreBeatmapAcceptance Acceptance { get; }

	/// <summary>Scopes score submission to a game client and its privileges.</summary>
	/// <param name="actor">The game client that submits.</param>
	/// <returns>A short-lived scope for the client.</returns>
	public ScopedScoreSubmitter ScopeTo(StableConnection actor)
	{
		return new ScopedScoreSubmitter(this, actor, actor.EffectivePrivileges(_clock.GetUtcNow()));
	}

	/// <summary>Validates and records a score a game client submitted, then publishes <see cref="ScoreSubmitted" />.</summary>
	/// <param name="actor">The game client that submits.</param>
	/// <param name="submission">The parsed submission.</param>
	/// <param name="beatmap">The beatmap the server knows for the submission, or <see langword="null" />.</param>
	/// <param name="client">The osu! client information sent with the submission.</param>
	/// <param name="replay">The seekable replay stream, or <see langword="null" /> for a failed play.</param>
	/// <param name="ct">A token that cancels the submission.</param>
	/// <returns>Ok when the score was stored; otherwise Rejected with the rejection.</returns>
	internal async Task<(ScoreSubmissionOutcome Outcome, ScoreRejectionReason? Rejection)> SubmitAsync(
		StableConnection actor, Submission submission, BeatmapChecksums? beatmap, SubmittedClient client,
		Stream? replay, CancellationToken ct)
	{
		var checkedBeatmap = beatmap ??
		                     (Acceptance.AcceptsUnknown(actor.Record, client.BeatmapHash)
			                     ? new BeatmapChecksums(client.BeatmapHash, null)
			                     : null);
		if (checkedBeatmap is null) return (ScoreSubmissionOutcome.Rejected, ScoreRejectionReason.UnknownBeatmap);

		var outcome = await Recorder.ValidateAndRecordAsync(
			actor.Record, actor.Session.Value.Fingerprint.Client!, submission, checkedBeatmap, client, replay,
			ct);
		if (outcome.Rejection is { } rejection) return (ScoreSubmissionOutcome.Rejected, rejection);

		Events.Publish(new ScoreSubmitted(actor.Record, outcome.Score!, outcome.Stats!));
		return (ScoreSubmissionOutcome.Ok, null);
	}
}