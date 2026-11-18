using Basil.Application.Shared.Paging;
using Basil.Domain.Moderation;
using Basil.Domain.Users;

namespace Basil.Application.Users.Persistence;

public interface IModerationLogRepository
{
	Task AppendAsync(ModerationData entry);

	ValueTask<Page<ModerationLog>> ListAsync(UserRecord target, Interval<DateTimeOffset>? within,
		PageRequest page, CancellationToken ct = default);
}