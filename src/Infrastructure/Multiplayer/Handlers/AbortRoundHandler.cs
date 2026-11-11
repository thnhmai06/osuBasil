using Basil.Application.Common;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="AbortRound" /> on the room for the connection that asked.</summary>
internal sealed class AbortRoundHandler : IRequestHandler<AbortRound, RoomOutcome>
{
	/// <inheritdoc />
	public ValueTask<RoomOutcome> Handle(AbortRound request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Rounds.AbortAsync());
	}
}