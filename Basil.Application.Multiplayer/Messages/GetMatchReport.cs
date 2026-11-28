using Basil.Application.Sessions.Objects;
using Basil.Domain.Multiplayer.Report;
using Basil.Domain.Multiplayer.Report.Matches;
using Basil.Domain.Multiplayer.Report.Rounds;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

public sealed record GetMatchReport(Session? Actor, int MatchId, int From = 0) : IRequest<MatchReportSnapshot?>;

public sealed record MatchReportSnapshot(MatchRecord Match, int Total, IReadOnlyList<MatchReportEntry> Entries);

public sealed record MatchReportEntry(IMatchEntry Entry, IReadOnlyList<RoundScore> Scores);