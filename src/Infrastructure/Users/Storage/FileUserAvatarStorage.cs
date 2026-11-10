using Basil.Application.Users.Storage;
using Basil.Domain.Users;
using Basil.Infrastructure.Common.Storage;

namespace Basil.Infrastructure.Users.Storage;

/// <summary>Stores user avatar images as files in the data directory.</summary>
internal sealed class FileUserAvatarStorage(DataPaths paths) : IUserAvatarStorage
{
	private DataStorage Files => paths.Avatars;

	/// <inheritdoc />
	public Task SaveAsync(UserRecord user, Stream content)
	{
		return Files.SaveAsync($"{user.Id}", content, CancellationToken.None);
	}

	/// <inheritdoc />
	public Task<Stream?> OpenAsync(UserRecord user, CancellationToken cancellationToken = default)
	{
		return Task.FromResult<Stream?>(Files.Open($"{user.Id}"));
	}

	/// <inheritdoc />
	public Task DeleteAsync(UserRecord user)
	{
		Files.Delete($"{user.Id}");
		return Task.CompletedTask;
	}
}