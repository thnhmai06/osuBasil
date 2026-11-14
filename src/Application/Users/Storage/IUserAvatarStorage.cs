using Basil.Domain.Users;

namespace Basil.Application.Users.Storage;

/// <summary>Stores the avatar images of users.</summary>
public interface IUserAvatarStorage
{
	/// <summary>Stores the avatar of a user, replacing any avatar stored for them.</summary>
	/// <param name="user">The user whose avatar to store.</param>
	/// <param name="content">The image bytes.</param>
	Task SaveAsync(UserRecord user, Stream content);

	/// <summary>Opens the stored avatar of a user.</summary>
	/// <param name="user">The user whose avatar to open.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The image bytes, or <see langword="null" /> when the user has no avatar.</returns>
	Task<Stream?> OpenAsync(UserRecord user, CancellationToken ct = default);

	/// <summary>Deletes the avatar of a user; deleting a missing avatar does nothing.</summary>
	/// <param name="user">The user whose avatar to delete.</param>
	Task DeleteAsync(UserRecord user);
}