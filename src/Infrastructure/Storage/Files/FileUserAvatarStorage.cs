using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Users;

namespace Basil.Infrastructure.Storage.Files;

/// <summary>Stores the avatars of users — bare image bytes, one extension-less file per user.</summary>
internal sealed class FileUserAvatarStorage(DataPaths paths) : IUserAvatarStorage
{
	/// <inheritdoc />
	public Task SaveAsync(User user, Stream content, CancellationToken cancellationToken = default)
	{
		return FileStorage.SaveAsync(Path.Combine(paths.Avatars, $"{user.Id}"), content, cancellationToken);
	}

	/// <inheritdoc />
	public Task<Stream?> OpenAsync(User user, CancellationToken cancellationToken = default)
	{
		return Task.FromResult<Stream?>(FileStorage.Open(Path.Combine(paths.Avatars, $"{user.Id}")));
	}

	/// <inheritdoc />
	public Task DeleteAsync(User user, CancellationToken cancellationToken = default)
	{
		FileStorage.Delete(Path.Combine(paths.Avatars, $"{user.Id}"));
		return Task.CompletedTask;
	}
}
