namespace Basil.Domain.Authentication;

/// <summary>The kind of connection a session belongs to.</summary>
public enum SessionType : byte
{
	/// <summary>A session of the osu! (stable) game client.</summary>
	Stable = 1 << 0,

	/// <summary>A session of the osu! (stable) tournament client; a user may hold several.</summary>
	StableTourney = 1 << 1,

	/// <summary>A session of an IRC connection.</summary>
	Irc = 1 << 2,

	/// <summary>A session that signs in to the HTTP API.</summary>
	Api = 1 << 3
}