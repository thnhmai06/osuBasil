using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Services.Contracts.Multiplayer.Rooms;
using Basil.Application.Services.Contracts.Sessions;

namespace Basil.Application.Services.Implementations.Multiplayer.Rooms;

/// <summary>Keeps the members of a room's channel in step with the people in the room.</summary>
internal sealed class RoomChannelService(IChannelService channels) : IRoomChannelPort
{
	void IRoomChannelPort.Join(Room room, Connection connection)
	{
		channels.Members.Join(room.Channel, connection);
	}

	void IRoomChannelPort.Part(Room room, Connection connection)
	{
		channels.Members.Part(room.Channel, connection);
	}

	void IRoomChannelPort.Close(Room room)
	{
		channels.Close(room.Channel);
	}
}