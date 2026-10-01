using Basil.Application.Chat;
using Basil.Application.Sessions;
using Basil.Domain.Chat;

namespace Basil.Application.Multiplayer;

/// <summary>A multiplayer room's chat channel while the room is open.</summary>
public sealed class RoomChatChannelSession(Room room, TimeProvider time)
	: ChatChannelSession(new RoomChatChannel(room.Id, room.Match), time)
{
	/// <summary>Gets the room that owns this channel.</summary>
	public Room Room => room;

	/// <inheritdoc />
	/// <remarks>Seated players, observers, the creator, the referees and the server's bot read the channel.</remarks>
	public override bool CanRead(Connection connection)
	{
		return connection.Type is ConnectionType.Bot ||
		       (connection is BanchoConnection player && room.Slots.Find(player) is not null) ||
		       (connection is TourneyConnection observer && room.Observers.Contains(observer)) ||
		       room.IsManager(connection.User);
	}

	/// <inheritdoc />
	public override bool CanWrite(Connection connection)
	{
		return CanRead(connection);
	}
}