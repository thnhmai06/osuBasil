using Basil.Application.Common;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="KickPlayer" /> on the room for the connection that asked.</summary>
internal sealed class KickPlayerHandler : IRequestHandler<KickPlayer, Result<RoomOutcome>>
{
	/// <inheritdoc />
	public ValueTask<Result<RoomOutcome>> Handle(KickPlayer request, CancellationToken cancellationToken)
	{
		return new ValueTask<Result<RoomOutcome>>(
			request.Room.ScopeTo(request.Actor).Members.KickAsync(request.Player, cancellationToken));
	}
}
