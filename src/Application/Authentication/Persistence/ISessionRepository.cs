using System.Net;
using Basil.Application.Shared.Paging;
using Basil.Domain.Authentication;

using Basil.Domain.Users;

namespace Basil.Application.Authentication.Persistence;

public enum SessionState : byte
{
	Any,

	Active,

	Ended
}

public sealed record SessionQuery(
	UserRecord User,
	SessionState State,
	IReadOnlyList<SessionType>? Types,
	DateTimeOffset Now);

public sealed record StoredSession(
	long Id,
	int UserId,
	SessionType Type,
	IPAddress Ip,
	string? ClientInfo,
	DateTimeOffset StartedAt,
	DateTimeOffset? ExpiresAt);

public sealed record SessionActivity(long Id, DateTimeOffset LastActiveAt, DateTimeOffset? ExpiresAt);

public interface ISessionRepository
{
	Task CreateOrUpdateAsync(SessionRecord session);

	ValueTask<Page<SessionRecord>> ListAsync(SessionQuery query, PageRequest page,
		CancellationToken ct = default);

	Task<SessionRecord?> GetAsync(UserRecord user, long id, CancellationToken ct = default);

	Task<StoredSession?> FindByTokenAsync(SessionType type, Sha256 tokenHash, DateTimeOffset now,
		CancellationToken ct = default);

	Task EndAsync(UserRecord user, long? only, long? except, IReadOnlyList<SessionType> types, DateTimeOffset at,
		SessionEndReason reason);

	Task<int> EndOpenAsync(IReadOnlyList<SessionType> types, SessionEndReason reason);

	Task WriteActivityAsync(IReadOnlyCollection<SessionActivity> activity);

	Task<int> DeleteEndedBeforeAsync(DateTimeOffset cutoff);
}