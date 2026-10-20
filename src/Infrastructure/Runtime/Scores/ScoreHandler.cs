using Basil.Application.Objects.Events;
using Basil.Application.Objects.Scores;

namespace Basil.Infrastructure.Runtime.Scores;

internal sealed class ScoreHandler : IEventHandler<ScoreEvent>
{
	/// <inheritdoc />
	public async ValueTask HandleAsync(ScoreEvent @event, CancellationToken cancellationToken)
	{
		if (@event is ScoreSubmitted { Room: { } room, RoundScore: { } roundScore } submitted)
			await room.RecordScoreAsync(roundScore, submitted.Player, cancellationToken);
	}
}