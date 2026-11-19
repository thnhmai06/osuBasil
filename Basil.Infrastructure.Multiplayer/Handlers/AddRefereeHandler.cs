using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class AddRefereeHandler : IRequestHandler<AddReferee, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(AddReferee request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Authority.AddRefereeAsync(request.User));
	}
}