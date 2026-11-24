using Basil.Application.Users.Storage;
using Basil.Domain.Users;
using Basil.Infrastructure.Shared.Storage;

namespace Basil.Infrastructure.Users.Storage;

internal sealed class FileUserAvatarStorage(DataPaths paths) : IUserAvatarStorage
{
	private DataStorage Files => paths.Avatars;

	public Task SaveAsync(UserRecord user, Stream content, Func<Stream, CancellationToken, Task> accept,
		CancellationToken ct = default)
	{
		return Files.SaveCheckedAsync(content, async (stored, token) =>
		{
			await accept(stored, token);
			return $"{user.Id}";
		}, ct);
	}

	public Task<Stream?> OpenAsync(int userId, CancellationToken ct = default)
	{
		return Task.FromResult<Stream?>(Files.Open($"{userId}"));
	}

	public Task DeleteAsync(UserRecord user)
	{
		Files.Delete($"{user.Id}");
		return Task.CompletedTask;
	}
}