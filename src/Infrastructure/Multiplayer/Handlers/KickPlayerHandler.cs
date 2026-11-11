using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="KickPlayer" /> on the room for the connection that asked.</summary>
internal sealed class KickPlayerHandler : IRequestHandler<KickPlayer, RoomOutcome>
{
	/// <inheritdoc />
	public ValueTask<RoomOutcome> Handle(KickPlayer request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Members.KickAsync(request.Player));
	}
}