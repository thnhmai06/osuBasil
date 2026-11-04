using Basil.Application.Common.Events;
using Basil.Application.Scores.Capabilities;
using Basil.Application.Scores.Ports;
using Basil.Application.Scores.Objects.Events;
using Basil.Application.Users.Objects.Connections;

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
	/// <param name="clock">The clock the permissions of a scope are evaluated at.</param>
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

	/// <summary>Scopes score submission to a game client and its permissions.</summary>
	/// <param name="actor">The game client that submits.</param>
	/// <returns>A short-lived scope for the client.</returns>
	public ScopedScoreSubmitter ScopeTo(StableConnection actor)
	{
		return new ScopedScoreSubmitter(this, actor, actor.EffectivePermissions(_clock.GetUtcNow()));
	}
}