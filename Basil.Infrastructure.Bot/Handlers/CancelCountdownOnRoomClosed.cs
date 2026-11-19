using Basil.Application.Multiplayer.Objects.Events;
using Basil.Infrastructure.Bot.Workers;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

internal sealed class CancelCountdownOnRoomClosed(RoomCountdowns countdowns) : INotificationHandler<LobbyRoomClosed>
{
	public async ValueTask Handle(LobbyRoomClosed notification, CancellationToken ct)
	{
		await countdowns.CancelAsync(notification.Room, CountdownCancelReason.RoomClosed, false, ct);
	}
}