using System.Diagnostics.CodeAnalysis;

namespace Basil.Domain.Chat;

/// <summary>The chat channel of a multiplayer room.</summary>
public sealed class RoomChannel : Channel
{
	/// <summary>Initializes the channel of a room, named after the room's id.</summary>
	/// <param name="roomId">The id of the room.</param>
	/// <param name="topic">The topic, the room's name.</param>
	[SetsRequiredMembers]
	public RoomChannel(int roomId, string topic)
	{
		Name = $"mp_{roomId}";
		Topic = topic;
	}
}
