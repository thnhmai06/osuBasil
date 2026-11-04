using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Bot.Countdowns;

/// <summary>Cancels the countdown of a room when a round starts by hand.</summary>
internal sealed class CancelCountdownOnRoundStarted(RoomCountdowns countdowns) : INotificationHandler<RoomRoundStarted>
{
	/// <inheritdoc />
	public async ValueTask Handle(RoomRoundStarted notification, CancellationToken cancellationToken)
	{
		await countdowns.CancelAsync(notification.Room, CountdownCancelReason.RoundStarted, false, cancellationToken);
	}
}
