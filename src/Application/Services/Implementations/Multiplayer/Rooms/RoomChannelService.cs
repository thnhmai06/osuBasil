using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Services.Contracts.Multiplayer.Rooms;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Services.Contracts.Multiplayer;

namespace Basil.Application.Services.Implementations.Multiplayer.Rooms;

/// <summary>Keeps the members of a room's channel in step with the people in the room.</summary>
internal sealed class RoomChannelService(ILobby lobby, IChannelService channels) : IRoomChannelPort
{
	void IRoomChannelPort.Join(Room room, Connection connection)
	{
		channels.Members.Join(room.Channel, connection);
	}

	void IRoomChannelPort.Part(Room room, Connection connection)
	{
		channels.Members.Part(room.Channel, connection);
	}

	/// <summary>Removes a connection from the channel of every room, managers included.</summary>
	/// <param name="connection">The connection that leaves.</param>
	internal void LeaveAllChannels(Connection connection)
	{
		foreach (var room in lobby.Rooms.Values)
			channels.Members.Part(room.Channel, connection);
	}
}