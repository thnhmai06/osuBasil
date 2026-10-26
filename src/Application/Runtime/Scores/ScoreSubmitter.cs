using Basil.Application.Capabilities.Scores;
using Basil.Application.Runtime.Events;
using Basil.Application.Runtime.Sessions;

namespace Basil.Application.Runtime.Scores;

/// <summary>Takes the scores that osu! clients submit: validates and stores them, then publishes <see cref="ScoreSubmitted" />.</summary>
public sealed class ScoreSubmitter
{
	private readonly IScoreBeatmapAcceptance _acceptance;
	private readonly TimeProvider _clock;
	private readonly IEventPublisher<ScoreEvent> _events;
	private readonly ScoreRecorder _recorder;

	/// <summary>Creates the score submitter with the ports its work needs.</summary>
	/// <param name="recorder">Validates and stores a score.</param>
	/// <param name="events">The publisher for score events.</param>
	/// <param name="acceptance">Decides about scores on beatmaps the server does not have.</param>
	/// <param name="clock">The clock the permissions of a scope are evaluated at.</param>
	internal ScoreSubmitter(ScoreRecorder recorder, IEventPublisher<ScoreEvent> events,
		IScoreBeatmapAcceptance acceptance, TimeProvider clock)
	{
		_recorder = recorder;
		_events = events;
		_acceptance = acceptance;
		_clock = clock;
	}

	internal ScoreRecorder Recorder => _recorder;

	internal IEventPublisher<ScoreEvent> Events => _events;

	internal IScoreBeatmapAcceptance Acceptance => _acceptance;

	/// <summary>Scopes score submission to a game client and its permissions.</summary>
	/// <param name="by">The game client that submits.</param>
	/// <returns>A short-lived scope for the client.</returns>
	public ScopedScoreSubmitter ScopeTo(BanchoConnection by)
	{
		return new ScopedScoreSubmitter(this, by, by.EffectivePermissions(_clock.GetUtcNow()));
	}
}
