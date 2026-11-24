namespace Basil.Domain.Multiplayer.Room;

/// <summary>Limits that apply to every room.</summary>
public static class RoomRules
{
	/// <summary>The number of slots a room has, and the most players it can hold.</summary>
	public const int MaxPlayers = 16;

	/// <summary>The fewest slots a room can be set to hold.</summary>
	public const int MinPlayers = 2;

	/// <summary>How long the other players of a round have to load their beatmap once the first player has loaded, before the round stops waiting for them.</summary>
	public static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(60);
}