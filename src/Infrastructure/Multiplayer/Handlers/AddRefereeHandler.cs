using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="AddReferee" /> on the room for the connection that asked.</summary>
internal sealed class AddRefereeHandler : IRequestHandler<AddReferee, RoomOutcome>
{
	/// <inheritdoc />
	public ValueTask<RoomOutcome> Handle(AddReferee request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Authority.AddRefereeAsync(request.User));
	}
}