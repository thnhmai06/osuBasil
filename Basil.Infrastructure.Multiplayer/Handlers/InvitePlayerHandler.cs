using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Messages;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class InvitePlayerHandler : IRequestHandler<InvitePlayer, RoomInviteResult>
{
	public ValueTask<RoomInviteResult> Handle(InvitePlayer request, CancellationToken ct)
	{
		return new ValueTask<RoomInviteResult>(
			request.Room.ScopeTo(request.Actor).Members.InviteAsync(request.Target));
	}
}