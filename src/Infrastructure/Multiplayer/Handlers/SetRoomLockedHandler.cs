using Basil.Application.Common;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="SetRoomLocked" /> on the room for the connection that asked.</summary>
internal sealed class SetRoomLockedHandler : IRequestHandler<SetRoomLocked, Result<RoomOutcome>>
{
	/// <inheritdoc />
	public ValueTask<Result<RoomOutcome>> Handle(SetRoomLocked request, CancellationToken cancellationToken)
	{
		return new ValueTask<Result<RoomOutcome>>(
			request.Room.ScopeTo(request.Actor).Settings.SetLockedAsync(request.Locked, cancellationToken));
	}
}
