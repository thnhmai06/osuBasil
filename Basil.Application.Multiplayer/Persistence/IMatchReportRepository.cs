using Basil.Domain.Multiplayer.Report;
using Basil.Domain.Multiplayer.Report.Matches;

namespace Basil.Application.Multiplayer.Persistence;

public interface IMatchReportRepository
{
	ValueTask<MatchReport> GetAsync(MatchRecord match, CancellationToken ct = default);
}