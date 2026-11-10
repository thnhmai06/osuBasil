using Basil.Application.Common;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="MarkRoundLoaded" /> on the room for the connection that asked.</summary>
internal sealed class MarkRoundLoadedHandler : IRequestHandler<MarkRoundLoaded, Result<RoomOutcome>>
{
	/// <inheritdoc />
	public ValueTask<Result<RoomOutcome>> Handle(MarkRoundLoaded request, CancellationToken cancellationToken)
	{
		return new ValueTask<Result<RoomOutcome>>(
			request.Room.ScopeTo(request.Actor).Rounds.MarkLoadedAsync());
	}
}