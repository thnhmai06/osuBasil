using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class RemoveRefereeHandler : IRequestHandler<RemoveReferee, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(RemoveReferee request, CancellationToken ct)
	{
		return new ValueTask<RoomActionResult>(
			request.Room.ScopeTo(request.Actor).Authority.RemoveRefereeAsync(request.User));
	}
}