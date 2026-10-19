namespace Basil.Domain.Multiplayer.Matches;

/// <summary>A record of something that happened in a match at a point in time.</summary>
public interface IMatchEntry
{
	/// <summary>Gets or sets the match associated with this entry.</summary>
	MatchRecord Match { get; }

	/// <summary>Gets the date and time of this entry.</summary>
	DateTimeOffset Timestamp { get; }
}