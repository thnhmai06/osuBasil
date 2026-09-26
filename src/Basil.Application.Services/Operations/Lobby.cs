using Basil.Application.Contracts.Events;
using Basil.Application.Contracts.Registries;
using Basil.Application.Contracts.Repositories;
using Basil.Application.Models.Sessions;
using Basil.Domain.Multiplayer.Records;
using Basil.Domain.Multiplayer.Runtime;
using Basil.Domain.Users;

namespace Basil.Application.Services.Operations;

/// <summary>Creates and closes multiplayer rooms.</summary>
public sealed class Lobby(
	IRoomRegistry rooms,
	IChannelRegistry channels,
	IPlayerRegistry players,
	IRepository<int, Match> matchRepository,
	IIdAllocator<Match> matchIds,
	IIdAllocator<Room> roomIds,
	IEventDispatcher dispatcher)
{
	/// <summary>Gets every currently open room, for a lobby listing.</summary>
	public IEnumerable<Room> Observers => rooms.AllById.Values;

	/// <summary>Creates a match, its room, and its chat channel, and registers all three.</summary>
	/// <param name="creator">The player creating the room, or <see langword="null" /> for an unattended room.</param>
	/// <param name="name">The room's name.</param>
	/// <param name="password">The room's password, or an empty string for none.</param>
	/// <param name="settings">The room's initial settings, or <see langword="null" /> for the defaults.</param>
	/// <param name="cancellationToken">A token that cancels the creation.</param>
	/// <returns>The newly created room.</returns>
	public async Task<Room> CreateRoomAsync(User? creator, string name, string password,
		RoomSettings? settings = null, CancellationToken cancellationToken = default)
	{
		var match = new Match
		{
			Id = await matchIds.NextAsync(cancellationToken), Name = name, CreatedAt = DateTimeOffset.UtcNow,
			EndedAt = null
		};
		await matchRepository.SaveAsync(match, cancellationToken);

		var id = await roomIds.NextAsync(cancellationToken);
		var room = new Room { Id = id, Match = match, Creator = creator, Settings = settings ?? new RoomSettings() };
		if (!string.IsNullOrEmpty(password))
			room.ChangePassword(password);

		if (!rooms.TryAdd(room))
			throw new InvalidOperationException($"A room with id {id} is already registered.");

		channels.TryAdd(new ChannelSession { Name = room.Channel.Name });

		await FlushAsync(room, cancellationToken);
		return room;
	}

	/// <summary>Closes a room: removes every player, ends its match, and unregisters the room and its channel.</summary>
	/// <param name="roomId">The id of the room to close.</param>
	/// <param name="cancellationToken">A token that cancels the closure.</param>
	public async Task CloseRoomAsync(int roomId, CancellationToken cancellationToken = default)
	{
		if (await rooms.EnterAsync(roomId, cancellationToken) is not { } scope) return;

		Room room;
		await using (scope)
		{
			room = scope.Room;

			foreach (var player in room.Slots.Where(s => s.User is not null).Select(s => s.User!).ToList())
				room.Leave(player);

			room.Match.EndedAt = DateTimeOffset.UtcNow;
			await matchRepository.SaveAsync(room.Match, cancellationToken);

			rooms.Remove(roomId);
		}

		if (channels.AllByName.TryGetValue(room.Channel.Name, out var channelSession))
		{
			foreach (var memberId in channelSession.MemberIds)
				if (players.AllById.TryGetValue(memberId, out var session))
				{
					channelSession.Part(session);
					if (session is GameSession { RoomId: var memberRoomId } game && memberRoomId == roomId)
						game.RoomId = null;
				}

			await FlushAsync(channelSession, cancellationToken);
			channels.Remove(room.Channel.Name);
		}
	}

	private async Task FlushAsync(Room room, CancellationToken cancellationToken)
	{
		foreach (var domainEvent in room.DomainEvents)
			await dispatcher.DispatchAsync(domainEvent, cancellationToken);
		room.ClearDomainEvents();
	}

	private async Task FlushAsync(ChannelSession channel, CancellationToken cancellationToken)
	{
		foreach (var domainEvent in channel.DomainEvents)
			await dispatcher.DispatchAsync(domainEvent, cancellationToken);
		channel.ClearDomainEvents();
	}
}