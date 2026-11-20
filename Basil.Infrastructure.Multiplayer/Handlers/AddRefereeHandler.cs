using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Messages;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class AddRefereeHandler : IRequestHandler<AddReferee, RoomTargetResult>
{
	public ValueTask<RoomTargetResult> Handle(AddReferee request, CancellationToken ct)
	{
		return new ValueTask<RoomTargetResult>(
			request.Room.ScopeTo(request.Actor).Authority.AddRefereeAsync(request.User));
	}
}