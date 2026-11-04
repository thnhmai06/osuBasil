using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Bot.Countdowns;

/// <summary>Cancels the countdown of a room that closed.</summary>
internal sealed class CancelCountdownOnRoomClosed(RoomCountdowns countdowns) : INotificationHandler<LobbyRoomClosed>
{
	/// <inheritdoc />
	public async ValueTask Handle(LobbyRoomClosed notification, CancellationToken cancellationToken)
	{
		await countdowns.CancelAsync(notification.Room, CountdownCancelReason.RoomClosed, false, cancellationToken);
	}
}
