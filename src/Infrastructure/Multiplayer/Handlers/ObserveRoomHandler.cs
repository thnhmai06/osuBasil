using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="ObserveRoom" /> on the room for the connection that asked.</summary>
internal sealed class ObserveRoomHandler : IRequestHandler<ObserveRoom, RoomOutcome>
{
	/// <inheritdoc />
	public ValueTask<RoomOutcome> Handle(ObserveRoom request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Members.ObserverJoinAsync());
	}
}