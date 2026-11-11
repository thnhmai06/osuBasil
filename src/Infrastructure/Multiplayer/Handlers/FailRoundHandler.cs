using Basil.Application.Common;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="FailRound" /> on the room for the connection that asked.</summary>
internal sealed class FailRoundHandler : IRequestHandler<FailRound, RoomOutcome>
{
	/// <inheritdoc />
	public ValueTask<RoomOutcome> Handle(FailRound request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Rounds.FailAsync());
	}
}