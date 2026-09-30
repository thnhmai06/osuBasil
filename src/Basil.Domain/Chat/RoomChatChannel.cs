using Basil.Domain.Multiplayer;

namespace Basil.Domain.Chat;

/// <summary>The chat channel of a multiplayer room.</summary>
/// <param name="roomId">The id of the room.</param>
/// <param name="match">The match the room plays.</param>
public sealed class RoomChatChannel(int roomId, Match match) : ChatChannel($"mp_{roomId}")
{
	/// <inheritdoc />
	public override string Topic => match.Value.Name;
}