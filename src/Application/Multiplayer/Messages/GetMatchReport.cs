using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

public sealed record GetMatchReport(Connection? Actor, int MatchId, int From = 0) : IRequest<MatchReportSnapshot?>;

public sealed record MatchReportSnapshot(MatchRecord Match, int Total, IReadOnlyList<MatchReportEntry> Entries);

public sealed record MatchReportEntry(IMatchEntry Entry, IReadOnlyList<RoundScore> Scores);