using Basil.Application.Shared;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="MovePlayer" /> on the room for the connection that asked.</summary>
internal sealed class MovePlayerHandler : IRequestHandler<MovePlayer, RoomOutcome>
{
	/// <inheritdoc />
	public ValueTask<RoomOutcome> Handle(MovePlayer request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Slots.MoveAsync(request.Player, request.Index));
	}
}