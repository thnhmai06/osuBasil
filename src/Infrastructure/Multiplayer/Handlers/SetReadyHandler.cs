using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;
using Basil.Domain.Multiplayer;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class SetReadyHandler : IRequestHandler<SetReady, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(SetReady request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Slots.SetReadyAsync(request.Ready));
	}
}