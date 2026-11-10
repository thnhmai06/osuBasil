using Basil.Application.Common;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="LeaveRoom" /> on the room for the connection that asked.</summary>
internal sealed class LeaveRoomHandler : IRequestHandler<LeaveRoom, Result<RoomOutcome>>
{
	/// <inheritdoc />
	public ValueTask<Result<RoomOutcome>> Handle(LeaveRoom request, CancellationToken cancellationToken)
	{
		return new ValueTask<Result<RoomOutcome>>(
			request.Room.ScopeTo(request.Actor).Members.LeaveAsync());
	}
}