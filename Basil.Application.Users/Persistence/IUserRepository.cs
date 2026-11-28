using Basil.Application.Shared.Paging;
using Basil.Domain.Users;

namespace Basil.Application.Users.Persistence;

public interface IUserRepository
{
	Task<UserRecord> CreateAsync(UserData data);

	Task UpdateAsync(UserRecord user);

	Task DiscardAsync(UserRecord user);

	Task<UserRecord> ReplaceAsync(int id, UserData data);

	Task<bool> RenameAsync(UserRecord user, string name);

	ValueTask<UserRecord?> GetAsync(int id, CancellationToken ct = default);

	/// <summary>Gets the users with these ids; an id no user has is left out.</summary>
	Task<IReadOnlyList<UserRecord>> GetManyAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default);

	ValueTask<UserRecord?> GetByNameAsync(string name, CancellationToken ct = default);

	Task<Page<UserRecord>> ListAsync(UserQuery query, PageRequest page, CancellationToken ct = default);
}