using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Shared;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="BanPlayer" /> on the room for the connection that asked.</summary>
internal sealed class BanPlayerHandler : IRequestHandler<BanPlayer, RoomOutcome>
{
	/// <inheritdoc />
	public ValueTask<RoomOutcome> Handle(BanPlayer request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Members.BanAsync(request.Player));
	}
}