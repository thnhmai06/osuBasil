namespace Basil.Domain.Multiplayer.Room;

/// <summary>Limits that apply to every room.</summary>
public static class RoomRules
{
	/// <summary>The number of slots a room has, and the most players it can hold.</summary>
	public const int MaxPlayers = 16;

	/// <summary>The fewest slots a room can be set to hold.</summary>
	public const int MinPlayers = 2;
}