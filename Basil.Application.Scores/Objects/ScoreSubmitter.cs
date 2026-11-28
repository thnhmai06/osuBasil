using Basil.Application.Scores.Capabilities;
using Basil.Application.Scores.Objects.Events;
using Basil.Application.Scores.Logic;
using Basil.Application.Scores.Persistence;
using Basil.Application.Shared.Events;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Scores;

namespace Basil.Application.Scores.Objects;

public sealed class ScoreSubmitter
{
	private readonly TimeProvider _clock;

	internal ScoreSubmitter(ScoreRecorder recorder, ScorePerformanceRequests performances,
		IEventPublisher<ScoreEvent> events, IScoreBeatmapAcceptance acceptance, TimeProvider clock)
	{
		Recorder = recorder;
		Performances = performances;
		Events = events;
		Acceptance = acceptance;
		_clock = clock;
	}

	internal ScoreRecorder Recorder { get; }

	internal ScorePerformanceRequests Performances { get; }

	internal IEventPublisher<ScoreEvent> Events { get; }

	internal IScoreBeatmapAcceptance Acceptance { get; }

	public ScopedScoreSubmitter ScopeTo(StableSession actor)
	{
		return new ScopedScoreSubmitter(this, actor, actor.EffectivePrivileges(_clock.GetUtcNow()));
	}

	internal async Task<(ScoreSubmissionResult Outcome, SubmissionRejectionReason? Rejection, ScoreReceipt? Receipt)>
		SubmitAsync(
			StableSession actor, Submission submission, BeatmapChecksums? beatmap, SubmittedClient client,
			Stream? replay, CancellationToken ct)
	{
		var checkedBeatmap = beatmap ??
		                     (Acceptance.AcceptsUnknown(actor.Record, client.BeatmapHash)
			                     ? new BeatmapChecksums(client.BeatmapHash, null)
			                     : null);
		if (checkedBeatmap is null)
			return (ScoreSubmissionResult.Rejected, SubmissionRejectionReason.UnknownBeatmap, null);

		var loginClient = actor.State.Record.Value.Fingerprint.Client!;
		if (submission.Validate(loginClient, checkedBeatmap, actor.Record.Value.Name, client) is { } invalid)
			return (ScoreSubmissionResult.Rejected, invalid, null);

		var (key, pp) = await Performances.RequestAndWaitAsync(submission.Score, ct);
		ScoreOutcome outcome;
		try
		{
			outcome = await Recorder.RecordAsync(actor.Record, loginClient, submission, pp, replay, ct);
		}
		catch
		{
			if (key is { } failed) Performances.Forget(failed);
			throw;
		}

		if (outcome.Rejection is { } rejection)
		{
			if (key is { } refused) Performances.Forget(refused);
			return (ScoreSubmissionResult.Rejected, rejection, null);
		}

		if (key is { } waiting) await Performances.StoredAsync(waiting, outcome.Score!.Id);

		Events.Publish(new ScoreSubmitted(actor.Record, outcome.Score!, outcome.Stats!));
		return (ScoreSubmissionResult.Ok, null,
			new ScoreReceipt(outcome.Score!, outcome.Stats!, outcome.PreviousBest));
	}
}