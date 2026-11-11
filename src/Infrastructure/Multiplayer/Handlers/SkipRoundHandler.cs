using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="SkipRound" /> on the room for the connection that asked.</summary>
internal sealed class SkipRoundHandler : IRequestHandler<SkipRound, RoomOutcome>
{
	/// <inheritdoc />
	public ValueTask<RoomOutcome> Handle(SkipRound request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Rounds.SkipAsync());
	}
}