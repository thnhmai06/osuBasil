using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Messages;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class ObserveRoomHandler : IRequestHandler<ObserveRoom, LobbyResult>
{
	public ValueTask<LobbyResult> Handle(ObserveRoom request, CancellationToken ct)
	{
		return new ValueTask<LobbyResult>(
			request.Room.ScopeTo(request.Actor).Members.ObserverJoinAsync());
	}
}