using Basil.Application.Objects.Sessions;
using Basil.Application.Objects.Multiplayer.Rooms;
using Basil.Domain.Permissions;

namespace Basil.Application.Objects.Chat;

/// <summary>The rules of who may read and write each kind of channel.</summary>
internal static class ChannelRules
{
	/// <summary>Checks whether a connection may read a channel.</summary>
	internal static bool CanRead(Channel channel, Connection connection, DateTimeOffset now)
	{
		return channel switch
		{
			GeneralChannel general => connection.User.EffectivePermissions(now)
				.Privileges.Allows(general.Record.Value.ReadPrivileges),
			PmChannel pm => ReferenceEquals(connection.User, pm.Owner) &&
			                connection.Type is not ConnectionType.Tourney,
			SpectatorChannel spectator => ReferenceEquals(connection, spectator.Host) ||
			                              spectator.Host.Spectators.Contains(connection),
			RoomChannel room => InRoom(room.Room, connection, now) ||
			                    connection.User.Check(Permissions.RoomObserve, now) is Access.Allowed,
			_ => false
		};
	}

	/// <summary>Checks whether a connection may write to a channel.</summary>
	internal static bool CanWrite(Channel channel, Connection connection, DateTimeOffset now)
	{
		return channel switch
		{
			GeneralChannel general => connection.User.EffectivePermissions(now)
				.Privileges.Allows(general.Record.Value.WritePrivileges),
			PmChannel pm => connection.IsOpen && pm.Owner.IsOnline,
			SpectatorChannel spectator => ReferenceEquals(connection, spectator.Host) ||
			                              spectator.Host.Spectators.Contains(connection),
			RoomChannel room => InRoom(room.Room, connection, now) ||
			                    connection.User.Check(Permissions.ChatPostAnyRoom, now) is Access.Allowed,
			_ => false
		};
	}

	/// <summary>Checks whether a connection takes part in a room: seated, observing, managing it while allowed to join rooms, or managing every room.</summary>
	internal static bool InRoom(Room room, Connection connection, DateTimeOffset now)
	{
		return (connection is BanchoConnection player && room.Slots.Find(player) is not null)
		       || (connection is TourneyConnection observer && room.Members.Observers.Contains(observer))
		       || (room.Authority.IsManagedBy(connection.Record) &&
		           connection.User.Check(Permissions.RoomJoin, now) is Access.Allowed)
		       || connection.User.Check(Permissions.RoomManageAny, now) is Access.Allowed;
	}
}