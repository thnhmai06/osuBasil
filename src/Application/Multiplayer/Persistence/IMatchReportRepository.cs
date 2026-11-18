using Basil.Domain.Multiplayer;

namespace Basil.Application.Multiplayer.Persistence;

public interface IMatchReportRepository
{
	ValueTask<MatchReport> GetAsync(MatchRecord match, CancellationToken ct = default);
}