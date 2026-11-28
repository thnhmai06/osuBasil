using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Messages;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class KickPlayerHandler : IRequestHandler<KickPlayer, RoomTargetResult>
{
	public ValueTask<RoomTargetResult> Handle(KickPlayer request, CancellationToken ct)
	{
		return new ValueTask<RoomTargetResult>(
			request.Room.ScopeTo(request.Actor).Members.KickAsync(request.PlayerId));
	}
}