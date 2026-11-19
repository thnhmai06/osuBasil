using Basil.Application.Multiplayer.Registry;
using Basil.Application.Scores.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class ScoreHandler(Lobby lobby) : INotificationHandler<ScoreSubmitted>
{
	public ValueTask Handle(ScoreSubmitted @event, CancellationToken ct)
	{
		foreach (var room in lobby.Rooms.Values)
			room.ReceiveScore(@event.Score, @event.Player);

		return ValueTask.CompletedTask;
	}
}