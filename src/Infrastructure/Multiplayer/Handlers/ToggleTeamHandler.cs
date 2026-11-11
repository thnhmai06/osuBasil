using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Shared;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="ToggleTeam" /> on the room for the connection that asked.</summary>
internal sealed class ToggleTeamHandler : IRequestHandler<ToggleTeam, RoomOutcome>
{
	/// <inheritdoc />
	public ValueTask<RoomOutcome> Handle(ToggleTeam request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Slots.ToggleTeamAsync());
	}
}