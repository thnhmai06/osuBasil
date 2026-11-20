using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Basil.Infrastructure.Bot.Workers;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

internal sealed class StartRoomCountdownHandler(ISender sender, RoomCountdowns countdowns)
	: IRequestHandler<StartRoomCountdown, RoomActionResult>
{
	public async ValueTask<RoomActionResult> Handle(StartRoomCountdown request, CancellationToken ct)
	{
		var allowed = await sender.Send(new CheckRoundStart(request.Actor, request.Room), ct);
		return allowed == RoomActionResult.Ok
			? await countdowns.StartAsync(request.Actor, request.Room, request.Duration, request.StartsRound,
				ct)
			: allowed;
	}
}