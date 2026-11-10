using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks Multiplayer for the report of a match: the match and its entries in time order.</summary>
/// <param name="Actor">The connection that asks, or <see langword="null" /> for a caller without a session.</param>
/// <param name="MatchId">The id of the match.</param>
/// <param name="From">The index of the first entry to return; earlier entries are left out.</param>
/// <remarks>
///     The answer is <see langword="null" /> when the match does not exist or the caller may not view it, so that the two
///     cases cannot be told apart. A caller without a session sees the matches that are not private.
/// </remarks>
public sealed record GetMatchReport(Connection? Actor, int MatchId, int From = 0) : IRequest<MatchReportSnapshot?>;

/// <summary>The answer to <see cref="GetMatchReport" />.</summary>
/// <param name="Match">The match as it is now.</param>
/// <param name="Total">How many entries the match has.</param>
/// <param name="Entries">The entries from the requested index on, in time order.</param>
public sealed record MatchReportSnapshot(MatchRecord Match, int Total, IReadOnlyList<MatchReportEntry> Entries);

/// <summary>One entry of a match report.</summary>
/// <param name="Entry">A round or an event.</param>
/// <param name="Scores">The scores of the round, or an empty list for an event.</param>
public sealed record MatchReportEntry(IMatchEntry Entry, IReadOnlyList<RoundScore> Scores);