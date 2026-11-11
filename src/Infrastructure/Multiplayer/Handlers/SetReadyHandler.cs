using Basil.Application.Common;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="SetReady" /> on the room for the connection that asked.</summary>
internal sealed class SetReadyHandler : IRequestHandler<SetReady, RoomOutcome>
{
	/// <inheritdoc />
	public ValueTask<RoomOutcome> Handle(SetReady request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Slots.SetReadyAsync(request.Ready));
	}
}