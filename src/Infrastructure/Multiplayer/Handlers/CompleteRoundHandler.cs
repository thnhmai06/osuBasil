using Basil.Application.Shared;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="CompleteRound" /> on the room for the connection that asked.</summary>
internal sealed class CompleteRoundHandler : IRequestHandler<CompleteRound, RoomOutcome>
{
	/// <inheritdoc />
	public ValueTask<RoomOutcome> Handle(CompleteRound request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Rounds.CompleteAsync());
	}
}