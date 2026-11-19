using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Messages;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class BanPlayerHandler : IRequestHandler<BanPlayer, LobbyResult>
{
	public ValueTask<LobbyResult> Handle(BanPlayer request, CancellationToken ct)
	{
		return new ValueTask<LobbyResult>(
			request.Room.ScopeTo(request.Actor).Members.BanAsync(request.Player));
	}
}