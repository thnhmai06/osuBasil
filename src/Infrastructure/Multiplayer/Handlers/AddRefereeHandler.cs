using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;
using Basil.Domain.Multiplayer;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class AddRefereeHandler : IRequestHandler<AddReferee, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(AddReferee request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Authority.AddRefereeAsync(request.User));
	}
}