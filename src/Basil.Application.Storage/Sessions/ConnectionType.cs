namespace Basil.Application.Storage.Sessions;

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