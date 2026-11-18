using System.Net;
using Basil.Application.Shared.Paging;
using Basil.Domain.Authentication;

namespace Basil.Application.Authentication.Persistence;

public sealed record ConnectionActivity(long SessionId, DateTimeOffset LastActiveAt);

public interface IConnectionRepository
{
	Task OpenAsync(long sessionId, IPAddress ip, string? clientInfo, DateTimeOffset at, TimeSpan reopenWithin);

	Task CloseAsync(long sessionId, DateTimeOffset lastActiveAt, DateTimeOffset closedAt, SessionEndReason reason);

	Task WriteActivityAsync(IReadOnlyCollection<ConnectionActivity> activity);

	ValueTask<Page<ConnectionRecord>> ListAsync(long sessionId, PageRequest page,
		CancellationToken ct = default);

	Task<int> CloseOpenAsync(SessionEndReason reason);
}