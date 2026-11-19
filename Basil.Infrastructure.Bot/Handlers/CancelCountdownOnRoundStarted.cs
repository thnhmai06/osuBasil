using Basil.Application.Multiplayer.Objects.Events;
using Basil.Infrastructure.Bot.Workers;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

internal sealed class CancelCountdownOnRoundStarted(RoomCountdowns countdowns) : INotificationHandler<RoomRoundStarted>
{
	public async ValueTask Handle(RoomRoundStarted notification, CancellationToken ct)
	{
		await countdowns.CancelAsync(notification.Room, CountdownCancelReason.RoundStarted, false, ct);
	}
}