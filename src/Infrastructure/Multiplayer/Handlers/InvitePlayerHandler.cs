using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;
using Basil.Domain.Multiplayer;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class InvitePlayerHandler : IRequestHandler<InvitePlayer, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(InvitePlayer request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Members.InviteAsync(request.Target));
	}
}