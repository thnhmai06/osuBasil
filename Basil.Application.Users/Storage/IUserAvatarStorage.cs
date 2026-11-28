using Basil.Domain.Users;

namespace Basil.Application.Users.Storage;

public interface IUserAvatarStorage
{
	/// <summary>Stores a user's avatar once <paramref name="accept" />, shown the stored bytes, agrees to it; when it throws, nothing is stored.</summary>
	Task SaveAsync(UserRecord user, Stream content, Func<Stream, CancellationToken, Task> accept,
		CancellationToken ct = default);

	Task<Stream?> OpenAsync(int userId, CancellationToken ct = default);

	Task DeleteAsync(UserRecord user);
}