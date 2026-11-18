using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;
using Basil.Domain.Multiplayer;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class MarkRoundLoadedHandler : IRequestHandler<MarkRoundLoaded, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(MarkRoundLoaded request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Rounds.MarkLoadedAsync());
	}
}