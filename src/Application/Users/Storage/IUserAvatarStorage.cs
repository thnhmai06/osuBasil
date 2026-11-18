using Basil.Domain.Users;

namespace Basil.Application.Users.Storage;

public interface IUserAvatarStorage
{
	Task SaveAsync(UserRecord user, Stream content);

	Task<Stream?> OpenAsync(UserRecord user, CancellationToken ct = default);

	Task DeleteAsync(UserRecord user);
}