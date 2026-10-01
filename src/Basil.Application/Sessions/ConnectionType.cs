namespace Basil.Application.Sessions;

/// <summary>The kinds of client a user can connect with.</summary>
public enum ConnectionType : byte
{
	/// <summary>An osu! game client.</summary>
	Bancho,

	/// <summary>An osu!tourney spectator client.</summary>
	Tourney,

	/// <summary>An IRC client.</summary>
	Irc,

	/// <summary>The server's own bot.</summary>
	Bot
}

/// <summary>Rules that depend on the kind of connection.</summary>
public static class ConnectionTypeExtensions
{
	/// <summary>Gets a value that indicates whether one user may hold several connections of this kind at once.</summary>
	/// <param name="type">The kind of connection.</param>
	/// <returns><see langword="true" /> only for <see cref="ConnectionType.Tourney" />.</returns>
	public static bool AllowsMany(this ConnectionType type)
	{
		return type is ConnectionType.Tourney;
	}
}