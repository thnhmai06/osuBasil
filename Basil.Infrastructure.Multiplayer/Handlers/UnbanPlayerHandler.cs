using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class UnbanPlayerHandler : IRequestHandler<UnbanPlayer, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(UnbanPlayer request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Members.UnbanAsync(request.Player));
	}
}