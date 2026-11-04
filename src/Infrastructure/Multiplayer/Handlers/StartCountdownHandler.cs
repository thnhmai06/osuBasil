using Basil.Application.Common;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="StartCountdown" /> on the room for the connection that asked.</summary>
internal sealed class StartCountdownHandler : IRequestHandler<StartCountdown, Result<RoomOutcome>>
{
	/// <inheritdoc />
	public ValueTask<Result<RoomOutcome>> Handle(StartCountdown request, CancellationToken cancellationToken)
	{
		return new ValueTask<Result<RoomOutcome>>(
			request.Room.ScopeTo(request.Actor).Rounds.StartCountdownAsync(request.Length, request.StartsRound, cancellationToken));
	}
}
