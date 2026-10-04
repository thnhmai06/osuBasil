using Basil.Domain.Users;

namespace Basil.Application.Storage.Users;

/// <summary>Stores the avatar images of users.</summary>
public interface IAvatarStorage
{
	/// <summary>Stores the avatar of a user, replacing any avatar stored for them.</summary>
	/// <param name="user">The user whose avatar to store.</param>
	/// <param name="content">The image bytes.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task SaveAsync(User user, Stream content, CancellationToken cancellationToken = default);

	/// <summary>Opens the stored avatar of a user.</summary>
	/// <param name="user">The user whose avatar to open.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The image bytes, or <see langword="null" /> when the user has no avatar.</returns>
	Task<Stream?> OpenAsync(User user, CancellationToken cancellationToken = default);

	/// <summary>Deletes the avatar of a user; deleting a missing avatar does nothing.</summary>
	/// <param name="user">The user whose avatar to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAsync(User user, CancellationToken cancellationToken = default);
}