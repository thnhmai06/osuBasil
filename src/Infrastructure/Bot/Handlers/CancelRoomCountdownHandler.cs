using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Basil.Infrastructure.Bot.Workers;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

internal sealed class CancelRoomCountdownHandler(ISender sender, RoomCountdowns countdowns)
	: IRequestHandler<CancelRoomCountdown, RoomOutcome>
{
	public async ValueTask<RoomOutcome> Handle(CancelRoomCountdown request, CancellationToken ct)
	{
		var allowed = await sender.Send(new CheckRoundStart(request.Actor, request.Room), ct);
		if (allowed != RoomOutcome.Ok) return allowed;

		if (request.Room.IsClosed) return RoomOutcome.RoomClosed;

		return await countdowns.CancelAsync(request.Room, CountdownCancelReason.Aborted, false, ct)
			? RoomOutcome.Ok
			: RoomOutcome.NotInProgress;
	}
}