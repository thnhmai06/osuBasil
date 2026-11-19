using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Messages;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class UnbanPlayerHandler : IRequestHandler<UnbanPlayer, LobbyResult>
{
	public ValueTask<LobbyResult> Handle(UnbanPlayer request, CancellationToken ct)
	{
		return new ValueTask<LobbyResult>(
			request.Room.ScopeTo(request.Actor).Members.UnbanAsync(request.Player));
	}
}