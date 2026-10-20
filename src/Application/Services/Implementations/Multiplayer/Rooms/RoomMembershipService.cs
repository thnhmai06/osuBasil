using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Contracts.Multiplayer.Events;
using Basil.Application.Services.Contracts.Multiplayer.Rooms;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Users;

namespace Basil.Application.Services.Implementations.Multiplayer.Rooms;

/// <summary>Releases a closing connection from room memberships.</summary>
internal sealed class RoomMembershipService(
	ILobby lobby,
	RoomChannelService roomChannel,
	IUserRepository users,
	TimeProvider time) : IRoomMembershipService
{
	/// <inheritdoc />
	public async Task ReleaseAsync(Connection connection, CancellationToken cancellationToken = default)
	{
		if (connection is BanchoConnection player)
		{
			if (lobby.RoomOf(player) is { } room)
			{
				await using var scope = await room.EnterAsync(cancellationToken);
				if (scope is not null) room.Leave(player);
			}
		}
		else if (connection is TourneyConnection observer)
		{
			foreach (var room in lobby.Rooms.Values.Where(room => room.Members.Observers.Contains(observer)).ToArray())
			{
				await using var scope = await room.EnterAsync(cancellationToken);
				if (scope is not null) room.ObserverLeave(observer);
			}
		}

		// Managers stay in a room's channel after leaving their seat, and IRC referees join it directly.
		roomChannel.LeaveAllChannels(connection);
	}

	/// <inheritdoc />
	public async Task RemoveSuspendedAsync(UserSession session, CancellationToken cancellationToken = default)
	{
		if (session.Check(Permissions.RoomJoin, time.GetUtcNow()) is Access.Allowed) return;

		foreach (var connection in session.Connections)
			await ReleaseAsync(connection, cancellationToken);

		var by = await users.GetAsync(BasilBot.Id, cancellationToken) ?? session.User;
		foreach (var room in lobby.Rooms.Values.ToArray())
		{
			if (!room.Authority.Referees.Contains(session.User)) continue;

			await using var scope = await room.EnterAsync(cancellationToken);
			if (scope is null || !room.Authority.MutableReferees.Remove(session.User)) continue;

			room.Context.Events.Emit(new RoomRefereeRemoved(room, by, session.User));
		}
	}
}