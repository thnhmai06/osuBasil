using Basil.Application.Common;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="SetRoomHost" /> on the room for the connection that asked.</summary>
internal sealed class SetRoomHostHandler : IRequestHandler<SetRoomHost, RoomOutcome>
{
	/// <inheritdoc />
	public ValueTask<RoomOutcome> Handle(SetRoomHost request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Authority.SetHostAsync(request.Host));
	}
}