using Basil.Application.Shared;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="SetRoomLocked" /> on the room for the connection that asked.</summary>
internal sealed class SetRoomLockedHandler : IRequestHandler<SetRoomLocked, RoomOutcome>
{
	/// <inheritdoc />
	public ValueTask<RoomOutcome> Handle(SetRoomLocked request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Settings.SetLockedAsync(request.Locked));
	}
}