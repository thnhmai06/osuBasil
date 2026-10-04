namespace Basil.Domain.Multiplayer;

/// <summary>
///     Represents an entry associated with a match that occurred at a specific point in time.
/// </summary>
public interface IMatchRecord
{
	/// <summary>Gets or sets the match associated with this entry.</summary>
	Match Match { get; init; }

	/// <summary>Gets the date and time of this entry.</summary>
	DateTimeOffset Timestamp { get; }
}