using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="SetHasMap" /> on the room for the connection that asked.</summary>
internal sealed class SetHasMapHandler : IRequestHandler<SetHasMap, RoomOutcome>
{
	/// <inheritdoc />
	public ValueTask<RoomOutcome> Handle(SetHasMap request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Slots.SetHasMapAsync(request.Has));
	}
}