using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Basil.Infrastructure.Bot.Workers;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

internal sealed class CancelRoomCountdownHandler(ISender sender, RoomCountdowns countdowns)
	: IRequestHandler<CancelRoomCountdown, RoomActionResult>
{
	public async ValueTask<RoomActionResult> Handle(CancelRoomCountdown request, CancellationToken ct)
	{
		var allowed = await sender.Send(new CheckRoundStart(request.Actor, request.Room), ct);
		if (allowed != RoomActionResult.Ok) return allowed;

		if (request.Room.IsClosed) return RoomActionResult.RoomClosed;

		return await countdowns.CancelAsync(request.Room, CountdownCancelReason.Aborted, false, ct)
			? RoomActionResult.Ok
			: RoomActionResult.NotInProgress;
	}
}