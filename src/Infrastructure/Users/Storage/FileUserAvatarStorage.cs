using Basil.Application.Users.Persistence;
using Basil.Application.Users.Storage;
using Basil.Domain.Users;
using Basil.Infrastructure.Common.Storage;

namespace Basil.Infrastructure.Users.Storage;

/// <summary>Stores user avatar images as files in the data directory.</summary>
internal sealed class FileUserAvatarStorage(DataPaths paths) : IUserAvatarStorage
{
	private DataStorage Files => paths.Avatars;

	/// <inheritdoc />
	public Task SaveAsync(UserRecord user, Stream content, CancellationToken cancellationToken = default)
	{
		return Files.SaveAsync($"{user.Id}", content, cancellationToken);
	}

	/// <inheritdoc />
	public Task<Stream?> OpenAsync(UserRecord user, CancellationToken cancellationToken = default)
	{
		return Task.FromResult<Stream?>(Files.Open($"{user.Id}"));
	}

	/// <inheritdoc />
	public Task DeleteAsync(UserRecord user, CancellationToken cancellationToken = default)
	{
		Files.Delete($"{user.Id}");
		return Task.CompletedTask;
	}
}