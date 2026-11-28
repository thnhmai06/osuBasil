using Basil.Domain.Multiplayer.Report.Matches;

namespace Basil.Domain.Multiplayer.Report;

/// <summary>A match together with the entries recorded in it.</summary>
/// <param name="Match">The match.</param>
/// <param name="Records">The recorded entries, such as events and rounds.</param>
public sealed record MatchReport(MatchRecord Match, IReadOnlyCollection<IMatchEntry> Records);

/// <summary>An entry in the history of a match.</summary>
public interface IMatchEntry
{
	/// <summary>Gets the match the entry belongs to.</summary>
	MatchRecord Match { get; }

	/// <summary>Gets when the entry happened.</summary>
	DateTimeOffset Timestamp { get; }
}