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
		if (allowed != RoomActionResult.Ok) return allowed;

		// A countdown that starts a round is refused when a round could not start now.
		if (request.StartsRound && request.Room.CheckStart() is var refusal and not RoomActionResult.Ok) return refusal;

		return await countdowns.StartAsync(request.Actor, request.Room, request.Length, request.StartsRound, ct);
	}
}