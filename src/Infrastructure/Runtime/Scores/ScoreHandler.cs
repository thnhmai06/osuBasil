using Basil.Application.Runtime.Events;
using Basil.Application.Runtime.Scores;

namespace Basil.Infrastructure.Runtime.Scores;

/// <summary>Gives a submitted score to the round of the room it was played in.</summary>
internal sealed class ScoreHandler : IEventHandler<ScoreEvent>
{
	/// <inheritdoc />
	public async ValueTask HandleAsync(ScoreEvent @event, CancellationToken cancellationToken)
	{
		if (@event is ScoreSubmitted { Room: { } room, RoundScore: { } roundScore } submitted)
			await room.RecordScoreAsync(roundScore, submitted.Player, cancellationToken);
	}
}