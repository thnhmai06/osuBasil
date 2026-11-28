using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class MarkRoundLoadedHandler : IRequestHandler<MarkRoundLoaded, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(MarkRoundLoaded request, CancellationToken ct)
	{
		return new ValueTask<RoomActionResult>(
			request.Room.ScopeTo(request.Actor).Rounds.MarkLoadedAsync());
	}
}