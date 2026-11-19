using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class RoomRoundStartedRecorder(IRoundRepository rounds) : INotificationHandler<RoomRoundStarted>
{
	public async ValueTask Handle(RoomRoundStarted @event, CancellationToken ct)
	{
		await rounds.CreateOrUpdateAsync(@event.Round);
	}
}