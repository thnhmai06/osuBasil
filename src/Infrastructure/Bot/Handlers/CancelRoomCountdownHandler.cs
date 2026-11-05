using Basil.Application.Bot.Registry;
using Basil.Application.Common;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

/// <summary>Cancels the countdown of a <see cref="CancelRoomCountdown" /> when the connection may start rounds.</summary>
internal sealed class CancelRoomCountdownHandler(ISender sender, RoomCountdowns countdowns)
	: IRequestHandler<CancelRoomCountdown, Result<RoomOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<RoomOutcome>> Handle(CancelRoomCountdown request, CancellationToken cancellationToken)
	{
		var allowed = await sender.Send(new CheckRoundStart(request.Actor, request.Room), cancellationToken);
		if (!allowed.IsOk) return allowed;

		if (request.Room.IsClosed) return RoomOutcome.RoomClosed;

		return await countdowns.CancelAsync(request.Room, CountdownCancelReason.Aborted, false, cancellationToken)
			? RoomOutcome.Ok
			: RoomOutcome.NotInProgress;
	}
}
