namespace Basil.Domain.Authentication;

/// <summary>The kinds of client connections.</summary>
public enum SessionType : byte
{
	/// <summary>An osu! (stable) game client.</summary>
	Stable,

	/// <summary>An osu!tourney (stable) spectator client.</summary>
	StableTourney,

	/// <summary>An IRC client.</summary>
	Irc,

	/// <summary>A client of the HTTP API.</summary>
	Api
}

public static class SessionTypeExtensions
{
	public static bool IsOsuSession(this SessionType type)
	{
		return type is SessionType.Stable or SessionType.StableTourney;
	}
}