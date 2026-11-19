using Basil.Application.Multiplayer.Objects.Events;
using Basil.Infrastructure.Bot.Workers;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

internal sealed class CancelCountdownOnSettingsChanged(RoomCountdowns countdowns)
	: INotificationHandler<RoomSettingsChanged>
{
	public async ValueTask Handle(RoomSettingsChanged notification, CancellationToken ct)
	{
		if (notification.Change.AltersTheRound)
			await countdowns.CancelAsync(notification.Room, CountdownCancelReason.SettingsChanged, true,
				ct);
	}
}