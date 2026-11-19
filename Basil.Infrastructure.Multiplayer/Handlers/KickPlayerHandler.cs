using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Messages;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class KickPlayerHandler : IRequestHandler<KickPlayer, LobbyResult>
{
	public ValueTask<LobbyResult> Handle(KickPlayer request, CancellationToken ct)
	{
		return new ValueTask<LobbyResult>(
			request.Room.ScopeTo(request.Actor).Members.KickAsync(request.Player));
	}
}