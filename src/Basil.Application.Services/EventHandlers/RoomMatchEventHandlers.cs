using Basil.Application.Contracts.Events;
using Basil.Application.Contracts.Registries;
using Basil.Application.Contracts.Repositories;
using Basil.Application.Models.Sessions;
using Basil.Domain.Multiplayer.Records;
using Basil.Domain.Multiplayer.Runtime;
using Notification = Basil.Application.Models.Notifications;

namespace Basil.Application.Services.EventHandlers;

/// <summary>
///     Reacts to a room's settings and round-flow events: slot/settings changes and the loading,
///     skip, fail, and completion sequence of a round.
/// </summary>
/// <remarks>
///     A started round is recorded and attached to its room (<see cref="Room.CurrentRound" />), and
///     closed out when <see cref="RoundEnded" /> reports it.
/// </remarks>
public sealed class RoomMatchEventHandlers(
	IPlayerRegistry players,
	IRepository<int, Round> rounds,
	IIdAllocator<Round> roundIds) :
	IDomainEventHandler<SlotChanged>,
	IDomainEventHandler<SlotLocked>,
	IDomainEventHandler<SettingsChanged>,
	IDomainEventHandler<RoomLockChanged>,
	IDomainEventHandler<RoundStarted>,
	IDomainEventHandler<PlayerLoaded>,
	IDomainEventHandler<AllPlayersLoaded>,
	IDomainEventHandler<PlayerSkipped>,
	IDomainEventHandler<AllPlayersSkipped>,
	IDomainEventHandler<PlayerFailed>,
	IDomainEventHandler<PlayerCompleted>,
	IDomainEventHandler<RoundEnded>
{
	/// <inheritdoc />
	public Task HandleAsync(AllPlayersLoaded domainEvent, CancellationToken cancellationToken = default)
	{
		NotifyRoom(domainEvent.Room, new Notification.AllPlayersLoaded());
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task HandleAsync(AllPlayersSkipped domainEvent, CancellationToken cancellationToken = default)
	{
		NotifyRoom(domainEvent.Room, new Notification.AllPlayersSkipped());
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task HandleAsync(PlayerCompleted domainEvent, CancellationToken cancellationToken = default)
	{
		NotifyRoom(domainEvent.Room, new Notification.RoomUpdated(domainEvent.Room));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task HandleAsync(PlayerFailed domainEvent, CancellationToken cancellationToken = default)
	{
		NotifyRoom(domainEvent.Room, new Notification.PlayerFailed(domainEvent.Slot));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task HandleAsync(PlayerLoaded domainEvent, CancellationToken cancellationToken = default)
	{
		NotifyRoom(domainEvent.Room, new Notification.RoomUpdated(domainEvent.Room));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task HandleAsync(PlayerSkipped domainEvent, CancellationToken cancellationToken = default)
	{
		NotifyRoom(domainEvent.Room, new Notification.PlayerSkipped(domainEvent.Slot));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task HandleAsync(RoomLockChanged domainEvent, CancellationToken cancellationToken = default)
	{
		NotifyRoom(domainEvent.Room, new Notification.RoomUpdated(domainEvent.Room));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task HandleAsync(RoundEnded domainEvent, CancellationToken cancellationToken = default)
	{
		if (domainEvent.Round is { } round)
		{
			round.EndedAt = DateTimeOffset.UtcNow;
			round.Aborted = domainEvent.Aborted;
			await rounds.SaveAsync(round, cancellationToken);
		}

		NotifyRoom(domainEvent.Room,
			domainEvent.Aborted ? new Notification.RoundAborted() : new Notification.RoundCompleted());
	}

	/// <inheritdoc />
	public async Task HandleAsync(RoundStarted domainEvent, CancellationToken cancellationToken = default)
	{
		if (domainEvent.Room.Settings.BeatmapMd5 is { } beatmapMd5)
		{
			var round = new Round
			{
				Id = await roundIds.NextAsync(cancellationToken),
				Match = domainEvent.Room.Match,
				Settings = new MatchSettings(domainEvent.Room.Settings) { BeatmapMd5 = beatmapMd5 },
				OccurredAt = DateTimeOffset.UtcNow,
				EndedAt = null
			};

			await rounds.SaveAsync(round, cancellationToken);
			domainEvent.Room.TrackRound(round);
		}

		NotifyRoom(domainEvent.Room, new Notification.RoundStarted(domainEvent.Room));
	}

	/// <inheritdoc />
	public Task HandleAsync(SettingsChanged domainEvent, CancellationToken cancellationToken = default)
	{
		NotifyRoom(domainEvent.Room, new Notification.RoomUpdated(domainEvent.Room));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task HandleAsync(SlotChanged domainEvent, CancellationToken cancellationToken = default)
	{
		NotifyRoom(domainEvent.Room, new Notification.RoomUpdated(domainEvent.Room));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task HandleAsync(SlotLocked domainEvent, CancellationToken cancellationToken = default)
	{
		NotifyRoom(domainEvent.Room, new Notification.RoomUpdated(domainEvent.Room));

		// The evicted player no longer holds a slot, so the membership-wide notify above never
		// reaches them; tell them directly and clear their stale room membership.
		if (domainEvent.Evicted is { } evicted && players.AllById.TryGetValue(evicted.Id, out var session))
		{
			if (session is GameSession { RoomId: { } roomId } game && roomId == domainEvent.Room.Id)
				game.RoomId = null;
			session.Notify(new Notification.RoomUpdated(domainEvent.Room));
		}

		return Task.CompletedTask;
	}

	private void NotifyRoom(Room room, Notification.Notification notification)
	{
		foreach (var slot in room.Slots)
			if (slot.User is { } user && players.AllById.TryGetValue(user.Id, out var session))
				session.Notify(notification);
	}
}