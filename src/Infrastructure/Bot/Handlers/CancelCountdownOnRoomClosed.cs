using Basil.Infrastructure.Bot.Workers;
using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

/// <summary>Cancels the countdown of a room that closed.</summary>
internal sealed class CancelCountdownOnRoomClosed(RoomCountdowns countdowns) : INotificationHandler<LobbyRoomClosed>
{
	/// <inheritdoc />
	public async ValueTask Handle(LobbyRoomClosed notification, CancellationToken ct)
	{
		await countdowns.CancelAsync(notification.Room, CountdownCancelReason.RoomClosed, false, ct);
	}
}