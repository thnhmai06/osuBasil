using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="UnbanPlayer" /> on the room for the connection that asked.</summary>
internal sealed class UnbanPlayerHandler : IRequestHandler<UnbanPlayer, RoomOutcome>
{
	/// <inheritdoc />
	public ValueTask<RoomOutcome> Handle(UnbanPlayer request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Members.UnbanAsync(request.Player));
	}
}