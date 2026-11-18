using Basil.Application.Shared.Paging;
using Basil.Domain.Chat;

namespace Basil.Application.Chat.Persistence;

public interface IChannelRepository
{
	Task<IEnumerable<GeneralChannelRecord>> ListAsync(CancellationToken ct = default);

	Task CreateOrUpdateAsync(GeneralChannelRecord channel);

	Task DeleteAsync(GeneralChannelRecord channel);
}