using Basil.Application.Shared.Paging;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Multiplayer.Persistence;

public interface IMatchEventRepository
{
	Task RecordAsync(MatchEvent matchEvent);

	Task<IReadOnlyList<MatchEvent>> ListAsync(MatchRecord match, CancellationToken ct = default);
}