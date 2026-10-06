using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Sessions;

namespace Basil.Application.Services.Implementations.Multiplayer.Rooms;

/// <summary>Keeps the members of a room's channel in step with the people in the room.</summary>
internal sealed class RoomChannelService(ILobby lobby, IChannelService channels)
{
	/// <summary>Adds a connection to the room's channel.</summary>
	/// <param name="room">The room whose channel to join.</param>
	/// <param name="connection">The connection that joins.</param>
	internal void JoinChannel(Room room, Connection connection)
	{
		channels.Join(room.Channel, connection);
	}

	/// <summary>Removes a connection from the room's channel unless it manages the room.</summary>
	/// <param name="room">The room whose channel to leave.</param>
	/// <param name="connection">The connection that leaves.</param>
	internal void LeaveChannel(Room room, Connection connection)
	{
		if (!RoomRules.IsManager(room, connection.User)) channels.Part(room.Channel, connection);
	}

	/// <summary>Removes a connection from the channel of every room, managers included.</summary>
	/// <param name="connection">The connection that leaves.</param>
	internal void LeaveAllChannels(Connection connection)
	{
		foreach (var room in lobby.Rooms)
			channels.Part(room.Channel, connection);
	}
}
