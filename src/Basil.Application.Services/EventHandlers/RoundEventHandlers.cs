using Basil.Application.Contracts.Events;
using Basil.Application.Contracts.Repositories;
using Basil.Application.Models.Events;
using Basil.Application.Models.Events.Multiplayer;
using Basil.Application.Models.Multiplayer;
using Basil.Domain.Multiplayer;
using Notification = Basil.Application.Models.Notifications;

namespace Basil.Application.Services.EventHandlers;

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
			case RoundStarted started:
				await rounds.SaveAsync(started.Round, cancellationToken);
				NotifyRoom(started.Room, new Notification.RoundStarted(started.Room));
				break;
			case RoundAborted aborted:
				await rounds.SaveAsync(aborted.Round, cancellationToken);
				NotifyRoom(aborted.Room, new Notification.RoundAborted());
				break;
			case RoundCompleted completed:
				await rounds.SaveAsync(completed.Round, cancellationToken);
				NotifyRoom(completed.Room, new Notification.RoundCompleted());
				break;
			case PlayerLoaded loaded:
				NotifyRoom(loaded.Room, new Notification.RoomUpdated(loaded.Room));
				break;
			case AllPlayersLoaded _:
				NotifyRoom(domainEvent.Room, new Notification.AllPlayersLoaded());
				break;
			case PlayerSkipped skipped:
				NotifyRoom(skipped.Room, new Notification.PlayerSkipped(skipped.Slot));
				break;
			case AllPlayersSkipped _:
				NotifyRoom(domainEvent.Room, new Notification.AllPlayersSkipped());
				break;
			case PlayerFailed failed:
				NotifyRoom(failed.Room, new Notification.PlayerFailed(failed.Slot));
				break;
			case PlayerCompleted completed:
				NotifyRoom(completed.Room, new Notification.RoomUpdated(completed.Room));
				break;
		}
	}

	private void NotifyRoom(Room room, Notification.Notification notification)
	{
		foreach (var slot in room.Slots)
			slot.Session?.Notify(notification);
	}
}