using Basil.Application.Objects.Multiplayer;
using Basil.Application.Objects.Multiplayer.Events;
using Basil.Application.Objects.Multiplayer.Rooms;
using Basil.Application.Objects.Sessions;
using Basil.Application.Storage.Users;
using Basil.Domain.Permissions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Multiplayer.Rooms;

/// <summary>Releases a closing connection from room memberships.</summary>
internal sealed class RoomMembershipService(
	Lobby lobby,
	IUserRepository users,
	TimeProvider time) : IRoomMembershipService
{
	/// <inheritdoc />
	public async Task ReleaseAsync(Connection connection, CancellationToken cancellationToken = default)
	{
		switch (connection)
		{
			case BanchoConnection player:
			{
				if (lobby.RoomOf(player) is { } room)
				{
					await using var scope = await room.EnterAsync(cancellationToken);
					if (scope is not null) room.Leave(player);
				}

				break;
			}
			case TourneyConnection observer:
			{
				foreach (var room in lobby.Rooms.Values.Where(room => room.Members.Observers.Contains(observer))
					         .ToArray())
				{
					await using var scope = await room.EnterAsync(cancellationToken);
					if (scope is not null) room.ObserverLeave(observer);
				}

				break;
			}
		}

		// Managers stay in a room's channel after leaving their seat, and IRC referees join it directly.
		foreach (var room in lobby.Rooms.Values)
			room.Channel.ScopeTo(connection).Part();
	}

	/// <inheritdoc />
	public async Task RemoveSuspendedAsync(UserSession session, CancellationToken cancellationToken = default)
	{
		if (session.Check(Permissions.RoomJoin, time.GetUtcNow()) is Access.Allowed) return;

		foreach (var connection in session.Connections)
			await ReleaseAsync(connection, cancellationToken);

		await RemoveRefereeAsync(session.User, cancellationToken);
	}

	/// <inheritdoc />
	public async Task RemoveRefereeAsync(UserRecord user, CancellationToken cancellationToken = default)
	{
		var by = await users.GetAsync(BasilBot.Id, cancellationToken) ?? user;
		foreach (var room in lobby.Rooms.Values.ToArray())
		{
			if (!room.Authority.Referees.Contains(user)) continue;

			await using var scope = await room.EnterAsync(cancellationToken);
			if (scope is null || !room.Authority.MutableReferees.Remove(user)) continue;

			room.Context.Events.Publish(new RoomRefereeRemoved(room, by, user));
		}
	}
}