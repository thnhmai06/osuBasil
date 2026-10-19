namespace Basil.Domain.Multiplayer.Matches;

/// <summary>
///     What happened in a match: its rounds, each with its result, and its lifecycle events, in the order they
///     happened.
/// </summary>
/// <param name="Match">The match this report describes.</param>
/// <param name="Records">The rounds and events of the match, ordered by <see cref="IMatchEntry.Timestamp" />.</param>
public sealed record MatchReport(MatchRecord Match, IReadOnlyList<IMatchEntry> Records);