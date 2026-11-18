using Basil.Application.Shared.Paging;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Persistence;

public interface IMatchRepository
{
	Task<MatchRecord> CreateAsync(MatchData data);

	Task UpdateAsync(MatchRecord match);

	ValueTask<MatchRecord?> GetAsync(int id, CancellationToken ct = default);

	Task<Page<MatchRecord>> ListAsync(MatchQuery query, PageRequest page,
		CancellationToken ct = default);

	Task<bool> IsVisibleToAsync(MatchRecord match, UserRecord viewer, CancellationToken ct = default);
}