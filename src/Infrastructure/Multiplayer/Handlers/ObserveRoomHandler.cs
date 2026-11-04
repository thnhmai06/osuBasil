using Basil.Application.Common;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="ObserveRoom" /> on the room for the connection that asked.</summary>
internal sealed class ObserveRoomHandler : IRequestHandler<ObserveRoom, Result<RoomOutcome>>
{
	/// <inheritdoc />
	public ValueTask<Result<RoomOutcome>> Handle(ObserveRoom request, CancellationToken cancellationToken)
	{
		return new ValueTask<Result<RoomOutcome>>(
			request.Room.ScopeTo(request.Actor).Members.ObserverJoinAsync(cancellationToken));
	}
}
