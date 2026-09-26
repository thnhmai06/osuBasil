using Basil.Application.Contracts.Events;
using Basil.Application.Contracts.Registries;
using Basil.Application.Contracts.Repositories;
using Basil.Application.Models.Notifications;
using Basil.Application.Services.Operations;
using Basil.Domain.Multiplayer.Records;
using Basil.Domain.Multiplayer.Runtime;
using Basil.Domain.Users;

namespace Basil.Application.Services.EventHandlers;

/// <summary>
///     Reacts to a room's membership and authority events: players joining, leaving, being
///     kicked/banned/invited, host transfer, and referee grants.
/// </summary>
/// <remarks>
///     Every membership change is recorded as a <see cref="MatchEvent" /> and re-announces the room's
///     full state to its seated players, since the osu! client protocol has no per-field room delta.
///     A room left with no one seated is closed through <see cref="Lobby" />.
/// </remarks>
public sealed class RoomMembershipEventHandlers(
	IPlayerRegistry players,
	Lobby lobby,
	IRepository<int, MatchEvent> matchEvents) :
	IDomainEventHandler<PlayerJoined>,
	IDomainEventHandler<PlayerLeft>,
	IDomainEventHandler<PlayerKicked>,
	IDomainEventHandler<PlayerBanned>,
	IDomainEventHandler<PlayerInvited>,
	IDomainEventHandler<HostChanged>,
	IDomainEventHandler<RefereeAdded>,
	IDomainEventHandler<RefereeRemoved>
{
	/// <inheritdoc />
	public async Task HandleAsync(HostChanged domainEvent, CancellationToken cancellationToken = default)
	{
		await RecordAsync(domainEvent.Room, MatchEventType.HostGranted, target: domainEvent.Host,
			cancellationToken: cancellationToken);
		NotifyRoom(domainEvent.Room, new HostTransferred());
	}

	/// <inheritdoc />
	public Task HandleAsync(PlayerBanned domainEvent, CancellationToken cancellationToken = default)
	{
		NotifyRoom(domainEvent.Room, new RoomUpdated(domainEvent.Room));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task HandleAsync(PlayerInvited domainEvent, CancellationToken cancellationToken = default)
	{
		if (players.AllById.TryGetValue(domainEvent.Player.Id, out var session))
			session.Notify(new Invited(domainEvent.Room, domainEvent.Room.Creator?.Id ?? domainEvent.Player.Id));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task HandleAsync(PlayerJoined domainEvent, CancellationToken cancellationToken = default)
	{
		await RecordAsync(domainEvent.Room, MatchEventType.PlayerJoined, domainEvent.Player,
			cancellationToken: cancellationToken);
		NotifyRoom(domainEvent.Room, new RoomUpdated(domainEvent.Room));
	}

	/// <inheritdoc />
	public async Task HandleAsync(PlayerKicked domainEvent, CancellationToken cancellationToken = default)
	{
		await RecordAsync(domainEvent.Room, MatchEventType.Kicked, target: domainEvent.Player,
			cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public async Task HandleAsync(PlayerLeft domainEvent, CancellationToken cancellationToken = default)
	{
		await RecordAsync(domainEvent.Room, MatchEventType.PlayerLeft, domainEvent.Player,
			cancellationToken: cancellationToken);
		NotifyRoom(domainEvent.Room, new RoomUpdated(domainEvent.Room));

		if (domainEvent.Room.Slots.All(s => s.User is null))
			await lobby.CloseRoomAsync(domainEvent.Room.Id, cancellationToken);
	}

	/// <inheritdoc />
	public async Task HandleAsync(RefereeAdded domainEvent, CancellationToken cancellationToken = default)
	{
		await RecordAsync(domainEvent.Room, MatchEventType.RefAdded, target: domainEvent.Referee,
			cancellationToken: cancellationToken);
		NotifyRoom(domainEvent.Room, new RoomUpdated(domainEvent.Room));
	}

	/// <inheritdoc />
	public async Task HandleAsync(RefereeRemoved domainEvent, CancellationToken cancellationToken = default)
	{
		await RecordAsync(domainEvent.Room, MatchEventType.RefRemoved, target: domainEvent.Referee,
			cancellationToken: cancellationToken);
		NotifyRoom(domainEvent.Room, new RoomUpdated(domainEvent.Room));
	}

	private Task RecordAsync(Room room, MatchEventType type, User? actor = null,
		User? target = null, CancellationToken cancellationToken = default)
	{
		return matchEvents.SaveAsync(
			new MatchEvent(room.Match, type, DateTimeOffset.UtcNow, actor, target), cancellationToken);
	}

	private void NotifyRoom(Room room, Notification notification)
	{
		foreach (var slot in room.Slots)
			if (slot.User is { } user && players.AllById.TryGetValue(user.Id, out var session))
				session.Notify(notification);
	}
}