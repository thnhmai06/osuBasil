using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class InvitePlayerHandler : IRequestHandler<InvitePlayer, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(InvitePlayer request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Members.InviteAsync(request.Target));
	}
}