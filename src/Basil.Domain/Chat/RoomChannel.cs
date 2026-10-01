using Basil.Domain.Multiplayer;

namespace Basil.Domain.Chat;

/// <summary>The chat channel of a multiplayer room.</summary>
/// <param name="roomId">The id of the room.</param>
/// <param name="match">The match the room plays.</param>
public sealed class RoomChannel(int roomId, Match match) : Channel($"mp_{roomId}")
{
	/// <inheritdoc />
	public override string Topic => match.Value.Name;
}