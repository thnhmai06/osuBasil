using Basil.Application.Scores.Objects.Events;
using Mediator;
using Basil.Application.Multiplayer.Registry;

namespace Basil.Application.Multiplayer.Handlers;

/// <summary>Offers every stored score to the rooms, which take it when one of their rounds awaits it.</summary>
public sealed class ScoreHandler(Lobby lobby) : INotificationHandler<ScoreSubmitted>
{
	/// <inheritdoc />
	public ValueTask Handle(ScoreSubmitted @event, CancellationToken cancellationToken)
	{
		foreach (var room in lobby.Rooms.Values)
			room.ReceiveScore(@event.Score, @event.Player);

		return ValueTask.CompletedTask;
	}
}