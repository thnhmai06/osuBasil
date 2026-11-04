namespace Basil.Application.Multiplayer.Objects;

/// <summary>Where a request to open a room comes from.</summary>
public enum RoomSource
{
	/// <summary>The creator's own osu! client opened the room in game, so that client is seated as its first host.</summary>
	Game = 1,

	/// <summary>The room was opened through the API, the console or BasilBot, so no osu! client is needed.</summary>
	Api = 2
}
