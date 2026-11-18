using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class ToggleTeamHandler : IRequestHandler<ToggleTeam, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(ToggleTeam request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Slots.ToggleTeamAsync());
	}
}