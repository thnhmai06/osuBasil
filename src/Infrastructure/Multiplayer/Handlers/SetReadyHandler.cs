using Basil.Application.Common;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="SetReady" /> on the room for the connection that asked.</summary>
internal sealed class SetReadyHandler : IRequestHandler<SetReady, Result<RoomOutcome>>
{
	/// <inheritdoc />
	public ValueTask<Result<RoomOutcome>> Handle(SetReady request, CancellationToken ct)
	{
		return new ValueTask<Result<RoomOutcome>>(
			request.Room.ScopeTo(request.Actor).Slots.SetReadyAsync(request.Ready));
	}
}