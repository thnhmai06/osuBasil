using Basil.Application.Multiplayer.Objects.Events;
using Basil.Infrastructure.Bot.Workers;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

internal sealed class CancelCountdownOnRoundAborted(RoomCountdowns countdowns) : INotificationHandler<RoomRoundAborted>
{
	public async ValueTask Handle(RoomRoundAborted notification, CancellationToken ct)
	{
		await countdowns.CancelAsync(notification.Room, CountdownCancelReason.RoundAborted, false, ct);
	}
}