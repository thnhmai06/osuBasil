using Basil.Application.Contracts.Events;
using Basil.Application.Contracts.Registries;
using Basil.Application.Contracts.Repositories;
using Basil.Application.Models.Multiplayer;
using Basil.Application.Models.Notifications;
using Basil.Application.Models.Sessions;
using Basil.Application.Services.Operations;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Notification = Basil.Application.Models.Notifications;

namespace Basil.Application.Services.EventHandlers;

/// <summary>
///     Reacts to room events by category: settings, slots, membership, authority, access, and closure.
/// </summary>
/// <remarks>
///     Uses pattern matching inside each category handler so related behaviour stays together while
///     still honouring the one-event-per-operation contract.
/// </remarks>
public sealed class RoomEventHandlers(
	ISessionRegistry<GameSession> games,
	Lobby lobby,
	IRepository<int, MatchEvent> matchEvents) :
	IEventHandler<RoomSettingsEvent>,
	IEventHandler<RoomSlotsEvent>,
	IEventHandler<RoomMembershipEvent>,
	IEventHandler<RoomAuthorityEvent>,
	IEventHandler<RoomAccessEvent>,
	IEventHandler<Basil.Application.Models.Multiplayer.RoomClosed>
{
	/// <inheritdoc />
	public Task HandleAsync(RoomSettingsEvent domainEvent, CancellationToken cancellationToken = default)
	{
		NotifyRoom(domainEvent.Room, new RoomUpdated(domainEvent.Room));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task HandleAsync(RoomSlotsEvent domainEvent, CancellationToken cancellationToken = default)
	{
		if (domainEvent is SlotLockChanged { Evicted: { } evicted } &&
		    games.AllByUser.TryGetValue(evicted.User, out var evictedSession))
			evictedSession.Notify(new RoomUpdated(domainEvent.Room));

		NotifyRoom(domainEvent.Room, new RoomUpdated(domainEvent.Room));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task HandleAsync(RoomMembershipEvent domainEvent, CancellationToken cancellationToken = default)
	{
		switch (domainEvent)
		{
			case PlayerJoined joined:
				await RecordAsync(joined.Room, MatchEventType.PlayerJoined, joined.Player.User,
					cancellationToken: cancellationToken);
				break;
			case PlayerLeft left:
				await RecordAsync(left.Room, MatchEventType.PlayerLeft, left.Player.User,
					cancellationToken: cancellationToken);
				break;
			case PlayerKicked kicked:
				await RecordAsync(kicked.Room, MatchEventType.Kicked, kicked.Player.User,
					cancellationToken: cancellationToken);
				break;
		}

		NotifyRoom(domainEvent.Room, new RoomUpdated(domainEvent.Room));

		if (domainEvent is PlayerKicked kickedEvent && games.AllByUser.TryGetValue(kickedEvent.Player.User, out var kickedSession))
			kickedSession.Notify(new RoomUpdated(domainEvent.Room));

		if (domainEvent.Room.Slots.All(s => s.Session is null))
			await lobby.CloseRoomAsync(domainEvent.Room.Id, cancellationToken);
	}

	/// <inheritdoc />
	public async Task HandleAsync(RoomAuthorityEvent domainEvent, CancellationToken cancellationToken = default)
	{
		switch (domainEvent)
		{
			case HostChanged hostChanged:
				await RecordAsync(hostChanged.Room, MatchEventType.HostGranted, target: hostChanged.Host?.User,
					cancellationToken: cancellationToken);
				NotifyRoom(domainEvent.Room, new HostTransferred());
				break;
			case RefereeAdded refereeAdded:
				await RecordAsync(refereeAdded.Room, MatchEventType.RefAdded, target: refereeAdded.Referee,
					cancellationToken: cancellationToken);
				NotifyRoom(domainEvent.Room, new RoomUpdated(domainEvent.Room));
				break;
			case RefereeRemoved refereeRemoved:
				await RecordAsync(refereeRemoved.Room, MatchEventType.RefRemoved, target: refereeRemoved.Referee,
					cancellationToken: cancellationToken);
				NotifyRoom(domainEvent.Room, new RoomUpdated(domainEvent.Room));
				break;
		}
	}

	/// <inheritdoc />
	public Task HandleAsync(RoomAccessEvent domainEvent, CancellationToken cancellationToken = default)
	{
		switch (domainEvent)
		{
			case PlayerBanned banned:
				NotifyRoom(domainEvent.Room, new RoomUpdated(domainEvent.Room));
				if (banned.Evicted is { } evicted && games.AllByUser.TryGetValue(evicted.User, out var session))
					session.Notify(new RoomUpdated(domainEvent.Room));
				break;
			case PlayerInvited invited:
				if (games.AllByUser.TryGetValue(invited.Player, out var gameSession))
					gameSession.Notify(new Invited(domainEvent.Room, domainEvent.Room.Creator ?? invited.Player));
				break;
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task HandleAsync(Basil.Application.Models.Multiplayer.RoomClosed domainEvent, CancellationToken cancellationToken = default)
	{
		foreach (var session in domainEvent.Evicted)
			session.Notify(new Notification.RoomClosed(domainEvent.Room));

		return Task.CompletedTask;
	}

	private Task RecordAsync(Room room, MatchEventType type, User? actor = null,
		User? target = null, CancellationToken cancellationToken = default)
	{
		return matchEvents.SaveAsync(
			new MatchEvent(room.Match, type, DateTimeOffset.UtcNow, actor, target), cancellationToken);
	}

	private void NotifyRoom(Room room, Notification.Notification notification)
	{
		foreach (var slot in room.Slots)
			slot.Session?.Notify(notification);
	}
}
