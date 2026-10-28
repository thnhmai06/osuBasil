namespace Basil.Domain.Auth;

/// <summary>The kinds of client connections and server-owned connection.</summary>
public enum ConnectionType : byte
{
	/// <summary>An osu! game client.</summary>
	Osu = 1,

	/// <summary>An osu!tourney spectator client.</summary>
	Tourney = 2,

	/// <summary>An IRC client.</summary>
	Irc = 4,

	/// <summary>A client of the HTTP API.</summary>
	Api = 8,

	/// <summary>The server's own connection for BasilBot; no client is behind it.</summary>
	Bot = 16
}
