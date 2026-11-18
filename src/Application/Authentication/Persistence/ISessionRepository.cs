using Basil.Application.Shared.Paging;
using Basil.Domain.Authentication;

using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Authentication.Persistence;

public enum SessionFilter : byte
{
	Any,

	Active,

	Ended
}

public sealed record SessionQuery(
	UserRecord User,
	SessionFilter State,
	IReadOnlyList<SessionType>? Types);

public sealed record SessionActivity(long Id, DateTimeOffset LastActiveAt);

public interface ISessionRepository
{
	Task CreateAsync(Session session);

	ValueTask<Page<Session>> ListAsync(SessionQuery query, PageRequest page,
		CancellationToken ct = default);

	Task<Session?> GetAsync(UserRecord user, long id, CancellationToken ct = default);

	Task<int?> FindOpenOwnerAsync(SessionType type, Sha256Hash tokenHash, CancellationToken ct = default);

	Task<Session?> FindOpenByTokenAsync(UserRecord user, SessionType type, Sha256Hash tokenHash,
		CancellationToken ct = default);

	Task<Session?> FindOpenAsync(UserRecord user, SessionType type, CancellationToken ct = default);

	Task EndAsync(long id, SessionEnded ended);

	Task EndAsync(UserRecord user, long? only, long? except, SessionEndReason reason, DateTimeOffset at);

	Task<int> EndOpenAsync(SessionType type, SessionEndReason reason, DateTimeOffset at);

	Task<int> EndSilentAsync(DateTimeOffset now);

	Task WriteActivityAsync(IReadOnlyCollection<SessionActivity> activity);

	Task<int> DeleteEndedBeforeAsync(DateTimeOffset cutoff);
}