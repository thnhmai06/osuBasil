using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="MarkRoundLoaded" /> on the room for the connection that asked.</summary>
internal sealed class MarkRoundLoadedHandler : IRequestHandler<MarkRoundLoaded, RoomOutcome>
{
	/// <inheritdoc />
	public ValueTask<RoomOutcome> Handle(MarkRoundLoaded request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Rounds.MarkLoadedAsync());
	}
}