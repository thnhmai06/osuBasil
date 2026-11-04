namespace Basil.Domain.Authentication;

/// <summary>The kinds of client connections.</summary>
public enum SessionType : byte
{
	/// <summary>An osu! (stable) game client.</summary>
	Stable = 1 << 0,

	/// <summary>An osu!tourney (stable) spectator client.</summary>
	StableTourney = 1 << 1,

	/// <summary>An IRC client.</summary>
	Irc = 1 << 2,

	/// <summary>A client of the HTTP API.</summary>
	Api = 1 << 3
}