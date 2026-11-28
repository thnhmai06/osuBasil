namespace Basil.Protocol.Game.Stable.Wire.Multiplayer;

/// <summary>Writes the link the osu! client opens to join a multiplayer room.</summary>
public static class RoomLink
{
	/// <summary>Writes the <c>osump://</c> address of a room.</summary>
	/// <param name="wireId">The room's id on the osu! wire.</param>
	/// <param name="password">The room's password, empty when it has none.</param>
	public static string Address(int wireId, string password)
	{
		return $"osump://{wireId}/{password}";
	}

	/// <summary>Writes the chat link that shows <paramref name="name"/> and opens the room.</summary>
	/// <param name="name">The text the link shows.</param>
	/// <param name="wireId">The room's id on the osu! wire.</param>
	/// <param name="password">The room's password, empty when it has none.</param>
	public static string Embed(string name, int wireId, string password)
	{
		return $"({name})[{Address(wireId, password)}]";
	}
}
