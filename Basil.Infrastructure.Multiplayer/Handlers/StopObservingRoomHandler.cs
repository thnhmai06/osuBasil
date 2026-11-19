using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Messages;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class StopObservingRoomHandler : IRequestHandler<StopObservingRoom, LobbyResult>
{
	public ValueTask<LobbyResult> Handle(StopObservingRoom request, CancellationToken ct)
	{
		return new ValueTask<LobbyResult>(
			request.Room.ScopeTo(request.Actor).Members.ObserverLeaveAsync());
	}
}