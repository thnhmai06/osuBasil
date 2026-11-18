using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class RemoveRefereeHandler : IRequestHandler<RemoveReferee, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(RemoveReferee request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Authority.RemoveRefereeAsync(request.User));
	}
}