using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class InvitePlayerHandler : IRequestHandler<InvitePlayer, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(InvitePlayer request, CancellationToken ct)
	{
		return new ValueTask<RoomActionResult>(
			request.Room.ScopeTo(request.Actor).Members.InviteAsync(request.Target));
	}
}