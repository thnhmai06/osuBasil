using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Messages;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class JoinRoomHandler : IRequestHandler<JoinRoom, LobbyResult>
{
	public ValueTask<LobbyResult> Handle(JoinRoom request, CancellationToken ct)
	{
		return new ValueTask<LobbyResult>(
			request.Room.ScopeTo(request.Actor).Members.JoinAsync(request.Password));
	}
}