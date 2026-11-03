namespace Basil.Domain.Auth;

/// <summary>The kinds of client connections and server-owned connection.</summary>
public enum SessionType : byte
{
	/// <summary>An osu! (stable) game client.</summary>
	Stable = 1 << 0,

	/// <summary>An osu!tourney (stable) spectator client.</summary>
	StableTourney = 1 << 1,

	/// <summary>An IRC client.</summary>
	Irc = 1 << 2,

	/// <summary>A client of the HTTP API.</summary>
	Api = 1 << 3,

	/// <summary>The server's own connection for BasilBot; no client is behind it.</summary>
	Bot = 1 << 4
}

public static class SessionTypeExtensions
{
	public static string Prefix(this SessionType type)
	{
		return type switch
		{
			SessionType.Stable => "osu-",
			SessionType.StableTourney => "trn-",
			SessionType.Api => "api-",
			SessionType.Irc => "irc-",
			SessionType.Bot => "bot-",
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
		};
	}
}