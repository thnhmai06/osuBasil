using Basil.Application.Bot.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

/// <summary>Cancels a countdown that would start a round when a setting of that round changes.</summary>
internal sealed class CancelCountdownOnSettingsChanged(RoomCountdowns countdowns)
	: INotificationHandler<RoomSettingsChanged>
{
	/// <inheritdoc />
	public async ValueTask Handle(RoomSettingsChanged notification, CancellationToken cancellationToken)
	{
		if (notification.Change.AltersTheRound)
			await countdowns.CancelAsync(notification.Room, CountdownCancelReason.SettingsChanged, true,
				cancellationToken);
	}
}