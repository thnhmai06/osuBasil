using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Shared;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="CloseRoom" /> on the room for the connection that asked.</summary>
internal sealed class CloseRoomHandler : IRequestHandler<CloseRoom, RoomOutcome>
{
	/// <inheritdoc />
	public ValueTask<RoomOutcome> Handle(CloseRoom request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).CloseAsync());
	}
}