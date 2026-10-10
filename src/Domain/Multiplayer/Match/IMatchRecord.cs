namespace Basil.Domain.Multiplayer.Match;

/// <summary>
///     An osu! TRT record unit.
/// </summary>
public interface IMatchRecord
{
	/// <summary>Gets or sets the match associated with this entry.</summary>
	Match Match { get; }

	/// <summary>Gets the date and time of this entry.</summary>
	DateTimeOffset Timestamp { get; }
}