using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class RoomRoundDecidedRecorder(IRoundRepository rounds) : INotificationHandler<RoomRoundDecided>
{
	public async ValueTask Handle(RoomRoundDecided @event, CancellationToken ct)
	{
		await rounds.CreateOrUpdateAsync(@event.Round);
	}
}