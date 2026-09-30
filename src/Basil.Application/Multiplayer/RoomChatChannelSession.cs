using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Application.Chat;
using Basil.Application.Sessions;

namespace Basil.Application.Multiplayer;

/// <summary>A multiplayer room's chat channel while the room is open.</summary>
public sealed class RoomChatChannelSession(Room room) : ChatChannelSession(new RoomChatChannel(room.Id, room.Match))
{
	/// <summary>Gets the room that owns this channel.</summary>
	public Room Room => room;

	/// <inheritdoc />
	/// <remarks>Seated players, the creator and the referees read the channel.</remarks>
	public override bool CanRead(Connection connection) =>
		(connection is BanchoConnection player && room.Slots.Find(player) is not null) ||
		room.IsReferee(connection.User);

	/// <inheritdoc />
	public override bool CanWrite(Connection connection) => CanRead(connection);
}