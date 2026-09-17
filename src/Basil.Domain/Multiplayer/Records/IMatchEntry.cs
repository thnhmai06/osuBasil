namespace Basil.Domain.Multiplayer.Records;

/// <summary>
///     Represents an entry associated with a match that occurred at a specific point in time.
/// </summary>
public interface IMatchEntry
{
	/// <summary>Gets the match associated with this entry.</summary>
	Match Match { get; init; }

	/// <summary>Gets the date and time when this entry occurred.</summary>
	DateTimeOffset OccurredAt { get; init; }
}