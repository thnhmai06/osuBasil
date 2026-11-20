using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Messages;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class RemoveRefereeHandler : IRequestHandler<RemoveReferee, RoomTargetResult>
{
	public ValueTask<RoomTargetResult> Handle(RemoveReferee request, CancellationToken ct)
	{
		return new ValueTask<RoomTargetResult>(
			request.Room.ScopeTo(request.Actor).Authority.RemoveRefereeAsync(request.User));
	}
}