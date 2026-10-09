using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Contracts.Scores;
using Basil.Infrastructure.Runtime.Events;

namespace Basil.Infrastructure.Runtime.Scores;

internal sealed class ScoreHandler(IRoomService rooms) : IEventHandler<ScoreEvent>
{
	/// <inheritdoc />
	public async ValueTask HandleAsync(ScoreEvent @event, CancellationToken cancellationToken)
	{
		if (@event is ScoreSubmitted { Room: { } room } submitted)
			await rooms.Rounds.RecordScoreAsync(room, submitted.Player, submitted.Score, cancellationToken);
	}
}