using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class KickPlayerHandler : IRequestHandler<KickPlayer, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(KickPlayer request, CancellationToken ct)
	{
		return new ValueTask<RoomActionResult>(
			request.Room.ScopeTo(request.Actor).Members.KickAsync(request.Player));
	}
}