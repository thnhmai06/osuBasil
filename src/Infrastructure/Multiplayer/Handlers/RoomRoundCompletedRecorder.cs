using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class RoomRoundCompletedRecorder(IRoundRepository rounds) : INotificationHandler<RoomRoundCompleted>
{
	public async ValueTask Handle(RoomRoundCompleted @event, CancellationToken ct)
	{
		await rounds.CreateOrUpdateAsync(@event.Round);
	}
}