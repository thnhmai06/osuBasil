using Basil.Domain.Multiplayer;

using Notification = Basil.Application.Multiplayer;
using Basil.Application.Common.Events;
using Basil.Application.Common.Notifications;
using Basil.Application.Common.Persistence;
using Basil.Application.Multiplayer.Events;
namespace Basil.Application.Multiplayer;

/// <summary>
///     Reacts to a room's round-flow events: starting, aborting, completing, and the loading,
///     skip, fail, and completion sequence of individual players.
/// </summary>
public sealed class RoundEventHandlers(IRepository<int, Round> rounds) :
	IEventHandler<RoundEvent>
{
	/// <inheritdoc />
	public async Task HandleAsync(RoundEvent domainEvent, CancellationToken cancellationToken = default)
	{
		switch (domainEvent)
		{
			case Events.RoundStarted started:
				await rounds.SaveAsync(started.Round, cancellationToken);
				NotifyRoom(started.Room, new Notification.RoundStarted(started.Room));
				break;
			case Events.RoundAborted aborted:
				await rounds.SaveAsync(aborted.Round, cancellationToken);
				NotifyRoom(aborted.Room, new Notification.RoundAborted());
				break;
			case Events.RoundCompleted completed:
				await rounds.SaveAsync(completed.Round, cancellationToken);
				NotifyRoom(completed.Room, new Notification.RoundCompleted());
				break;
			case PlayerLoaded loaded:
				NotifyRoom(loaded.Room, new Notification.RoomUpdated(loaded.Room));
				break;
			case Events.AllPlayersLoaded _:
				NotifyRoom(domainEvent.Room, new Notification.AllPlayersLoaded());
				break;
			case Events.PlayerSkipped skipped:
				NotifyRoom(skipped.Room, new Notification.PlayerSkipped(skipped.Slot));
				break;
			case Events.AllPlayersSkipped _:
				NotifyRoom(domainEvent.Room, new Notification.AllPlayersSkipped());
				break;
			case Events.PlayerFailed failed:
				NotifyRoom(failed.Room, new Notification.PlayerFailed(failed.Slot));
				break;
			case PlayerCompleted completed:
				NotifyRoom(completed.Room, new Notification.RoomUpdated(completed.Room));
				break;
		}
	}

	private void NotifyRoom(Room room, Common.Notifications.Notification notification)
	{
		foreach (var slot in room.Slots)
			slot.Session?.Notify(notification);
	}
}