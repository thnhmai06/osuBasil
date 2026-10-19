using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Services.Contracts.Multiplayer.Rooms;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Services.Implementations.Users;
using Basil.Domain.Users;

namespace Basil.Application.Services.Implementations.Chat;

/// <summary>The rules of who may read and write each kind of channel.</summary>
internal static class ChannelRules
{
	/// <summary>Checks whether a connection may read a channel.</summary>
	internal static bool CanRead(ChannelSession channel, Connection connection, DateTimeOffset now)
	{
		return channel switch
		{
			GeneralChannelSession g => PermissionRules.Effective(connection.Session, now)
				.Privileges.Allows(g.Channel.ReadPrivileges),
			PmChannelSession p => ReferenceEquals(connection.Session, p.Owner) &&
			                      connection.Type is not ConnectionType.Tourney,
			SpectatorChannelSession s => ReferenceEquals(connection, s.Host) || s.Host.Spectators.Contains(connection),
			RoomChannelSession r => InRoom(r.Room, connection, now) ||
			                        PermissionRules.Allows(connection, Permissions.RoomObserve, now),
			_ => false
		};
	}

	/// <summary>Checks whether a connection may write to a channel.</summary>
	internal static bool CanWrite(ChannelSession channel, Connection connection, DateTimeOffset now)
	{
		return channel switch
		{
			GeneralChannelSession g => PermissionRules.Effective(connection.Session, now)
				.Privileges.Allows(g.Channel.WritePrivileges),
			PmChannelSession => connection.IsOpen,
			SpectatorChannelSession s => ReferenceEquals(connection, s.Host) || s.Host.Spectators.Contains(connection),
			RoomChannelSession r => InRoom(r.Room, connection, now) ||
			                        PermissionRules.Allows(connection, Permissions.ChatPostAnyRoom, now),
			_ => false
		};
	}

	/// <summary>Checks whether a connection takes part in a room: seated, observing, managing it, or managing every room.</summary>
	internal static bool InRoom(Room room, Connection connection, DateTimeOffset now)
	{
		return (connection is BanchoConnection player && room.Slots.Find(player) is not null)
		       || (connection is TourneyConnection observer && room.Members.Observers.Contains(observer))
		       || room.Authority.IsManagedBy(connection.User)
		       || PermissionRules.Allows(connection, Permissions.RoomManageAny, now);
	}
}