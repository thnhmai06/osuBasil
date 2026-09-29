using Basil.Application.Contracts.Registries;
using Basil.Application.Contracts.Repositories;
using Basil.Application.Models.Multiplayer;
using Basil.Application.Models.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Services.Operations;

/// <summary>Creates and closes multiplayer rooms.</summary>
public sealed class Lobby(
	IRoomRegistry rooms,
	IChannelRegistry channels,
	IRepository<int, Match> matchRepository,
	IIdAllocator<Match> matchIds,
	IIdAllocator<Room> roomIds)
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
		MatchSettings? settings = null, CancellationToken cancellationToken = default)
	{
		var match = new Match
		{
			Id = await matchIds.NextAsync(cancellationToken),
			Name = name,
			CreatedAt = DateTimeOffset.UtcNow,
			EndedAt = null,
			Creator = creator
		};
		await matchRepository.SaveAsync(match, cancellationToken);

		var id = await roomIds.NextAsync(cancellationToken);
		var room = new Room { Id = id, Match = match, Settings = settings ?? new MatchSettings() };
		if (!string.IsNullOrEmpty(password))
			room.Password = password;

		if (!rooms.TryAdd(room))
			throw new InvalidOperationException($"A room with id {id} is already registered.");

		channels.TryAdd(room.Channel);

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
			room.Close();
			await matchRepository.SaveAsync(room.Match, cancellationToken);
			rooms.Remove(roomId);
		}

		foreach (var member in room.Channel.Members.ToArray())
			member.Part(room.Channel);

		channels.Remove(room.Channel.Name);
	}
}
