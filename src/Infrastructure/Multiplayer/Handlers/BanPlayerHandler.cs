using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;
using Basil.Domain.Multiplayer;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class BanPlayerHandler : IRequestHandler<BanPlayer, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(BanPlayer request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Members.BanAsync(request.Player));
	}
}