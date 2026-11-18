using Basil.Application.Scores.Capabilities;
using Basil.Application.Scores.Objects.Events;
using Basil.Application.Scores.Persistence;
using Basil.Application.Scores.Ports;
using Basil.Application.Shared.Events;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Scores;

namespace Basil.Application.Scores.Objects;

public sealed class ScoreSubmitter
{
	private readonly TimeProvider _clock;

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

	public ScopedScoreSubmitter ScopeTo(StableConnection actor)
	{
		return new ScopedScoreSubmitter(this, actor, actor.EffectivePrivileges(_clock.GetUtcNow()));
	}

	internal async Task<(ScoreSubmissionOutcome Outcome, ScoreRejectionReason? Rejection, ScoreReceipt? Receipt)> SubmitAsync(
		StableConnection actor, Submission submission, BeatmapChecksums? beatmap, SubmittedClient client,
		Stream? replay, CancellationToken ct)
	{
		var checkedBeatmap = beatmap ??
		                     (Acceptance.AcceptsUnknown(actor.Record, client.BeatmapHash)
			                     ? new BeatmapChecksums(client.BeatmapHash, null)
			                     : null);
		if (checkedBeatmap is null) return (ScoreSubmissionOutcome.Rejected, ScoreRejectionReason.UnknownBeatmap, null);

		var outcome = await Recorder.ValidateAndRecordAsync(
			actor.Record, actor.Session.Value.Fingerprint.Client!, submission, checkedBeatmap, client, replay,
			ct);
		if (outcome.Rejection is { } rejection) return (ScoreSubmissionOutcome.Rejected, rejection, null);

		Events.Publish(new ScoreSubmitted(actor.Record, outcome.Score!, outcome.Stats!));
		return (ScoreSubmissionOutcome.Ok, null,
			new ScoreReceipt(outcome.Score!, outcome.Stats!, outcome.PreviousBest));
	}
}