namespace Basil.Domain.Game;

/// <summary>Whose presence and stats an osu! client asks to receive; the values carry the game numbering.</summary>
public enum PresenceFilter : byte
{
	/// <summary>No one's presence or stats.</summary>
	None = 0,

	/// <summary>Everyone's presence and stats.</summary>
	All = 1,

	/// <summary>Only friends' presence and stats.</summary>
	Friends = 2
}

public static class PresenceFilterExtensions
{
	/// <summary>Gets whether a client with this filter receives the presence and stats of a user, who is the client's own user or its friend or neither.</summary>
	public static bool Includes(this PresenceFilter filter, bool isSelf, bool isFriend)
	{
		return isSelf || filter switch
		{
			PresenceFilter.All => true,
			PresenceFilter.Friends => isFriend,
			_ => false
		};
	}
}
