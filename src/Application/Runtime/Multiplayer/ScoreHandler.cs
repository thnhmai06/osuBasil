using Basil.Application.Runtime.Scores;
using Mediator;

namespace Basil.Application.Runtime.Multiplayer;

/// <summary>Offers every stored score to the rooms, which take it when one of their rounds awaits it.</summary>
internal sealed class ScoreHandler(Lobby lobby) : INotificationHandler<ScoreSubmitted>
{
	/// <inheritdoc />
	public ValueTask Handle(ScoreSubmitted @event, CancellationToken cancellationToken)
	{
		foreach (var room in lobby.Rooms.Values)
			room.ReceiveScore(@event.Score, @event.Player);

		return ValueTask.CompletedTask;
	}
}