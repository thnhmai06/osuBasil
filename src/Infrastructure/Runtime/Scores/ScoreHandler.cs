using Basil.Application.Objects.Events;
using Basil.Application.Objects.Multiplayer;
using Basil.Application.Objects.Scores;

namespace Basil.Infrastructure.Runtime.Scores;

internal sealed class ScoreHandler(IRoomService rooms) : IEventHandler<ScoreEvent>
{
	/// <inheritdoc />
	public async ValueTask HandleAsync(ScoreEvent @event, CancellationToken cancellationToken)
	{
		if (@event is ScoreSubmitted { Room: { } room, RoundScore: { } roundScore } submitted)
			await rooms.Rounds.RecordScoreAsync(room, roundScore, submitted.Player, cancellationToken);
	}
}