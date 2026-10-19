using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Contracts.Multiplayer.Rooms;
using Basil.Application.Services.Contracts.Sessions;

namespace Basil.Application.Services.Implementations.Multiplayer.Rooms;

/// <summary>Releases a closing connection from room memberships.</summary>
internal sealed class RoomMembershipService(ILobby lobby, RoomChannelService roomChannel) : IRoomMembershipService
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
}