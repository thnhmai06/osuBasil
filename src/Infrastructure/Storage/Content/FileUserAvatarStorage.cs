using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Users;
using Basil.Infrastructure.Storage.Common;
using Basil.Infrastructure.Storage.Common.Options;

namespace Basil.Infrastructure.Storage.Content;

/// <summary>Stores user avatar images as files in the data directory.</summary>
internal sealed class FileUserAvatarStorage(DataPaths paths) : IUserAvatarStorage
{
	private DataStorage Files => paths.Avatars;

	/// <inheritdoc />
	public Task SaveAsync(User user, Stream content, CancellationToken cancellationToken = default)
	{
		return Files.SaveAsync($"{user.Id}", content, cancellationToken);
	}

	/// <inheritdoc />
	public Task<Stream?> OpenAsync(User user, CancellationToken cancellationToken = default)
	{
		return Task.FromResult<Stream?>(Files.Open($"{user.Id}"));
	}

	/// <inheritdoc />
	public Task DeleteAsync(User user, CancellationToken cancellationToken = default)
	{
		Files.Delete($"{user.Id}");
		return Task.CompletedTask;
	}
}