using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Messages;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class BanPlayerHandler : IRequestHandler<BanPlayer, RoomTargetResult>
{
	public ValueTask<RoomTargetResult> Handle(BanPlayer request, CancellationToken ct)
	{
		return new ValueTask<RoomTargetResult>(
			request.Room.ScopeTo(request.Actor).Members.BanAsync(request.PlayerId));
	}
}