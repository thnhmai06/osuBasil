using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Messages;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class UnbanPlayerHandler : IRequestHandler<UnbanPlayer, RoomTargetResult>
{
	public ValueTask<RoomTargetResult> Handle(UnbanPlayer request, CancellationToken ct)
	{
		return new ValueTask<RoomTargetResult>(
			request.Room.ScopeTo(request.Actor).Members.UnbanAsync(request.PlayerId));
	}
}