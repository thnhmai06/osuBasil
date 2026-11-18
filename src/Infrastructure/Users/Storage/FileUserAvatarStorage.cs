using Basil.Application.Users.Storage;
using Basil.Domain.Users;
using Basil.Infrastructure.Shared.Storage;

namespace Basil.Infrastructure.Users.Storage;

internal sealed class FileUserAvatarStorage(DataPaths paths) : IUserAvatarStorage
{
	private DataStorage Files => paths.Avatars;

	public Task SaveAsync(UserRecord user, Stream content)
	{
		return Files.SaveAsync($"{user.Id}", content, CancellationToken.None);
	}

	public Task<Stream?> OpenAsync(UserRecord user, CancellationToken ct = default)
	{
		return Task.FromResult<Stream?>(Files.Open($"{user.Id}"));
	}

	public Task DeleteAsync(UserRecord user)
	{
		Files.Delete($"{user.Id}");
		return Task.CompletedTask;
	}
}