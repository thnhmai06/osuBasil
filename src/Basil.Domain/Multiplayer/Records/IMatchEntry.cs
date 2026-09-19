namespace Basil.Domain.Multiplayer.Records;

/// <summary>
///     Represents an entry associated with a match that occurred at a specific point in time.
/// </summary>
public interface IMatchEntry
{
	/// <summary>Gets or sets the match associated with this entry.</summary>
	Match Match { get; init; }

	/// <summary>Gets or sets the date and time when this entry occurred.</summary>
	DateTimeOffset OccurredAt { get; init; }
}