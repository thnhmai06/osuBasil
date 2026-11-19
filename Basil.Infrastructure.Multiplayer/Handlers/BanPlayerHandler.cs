using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class BanPlayerHandler : IRequestHandler<BanPlayer, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(BanPlayer request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Members.BanAsync(request.Player));
	}
}