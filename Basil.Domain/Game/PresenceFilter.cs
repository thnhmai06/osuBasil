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
