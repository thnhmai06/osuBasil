using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Bot.Countdowns;

/// <summary>Cancels the countdown of a room when its round is aborted.</summary>
internal sealed class CancelCountdownOnRoundAborted(RoomCountdowns countdowns) : INotificationHandler<RoomRoundAborted>
{
	/// <inheritdoc />
	public async ValueTask Handle(RoomRoundAborted notification, CancellationToken cancellationToken)
	{
		await countdowns.CancelAsync(notification.Room, CountdownCancelReason.RoundAborted, false, cancellationToken);
	}
}
