using Basil.Application.Storage.Contracts.Content;

namespace Basil.Infrastructure.Storage.Files;

/// <summary>Stores the seasonal main-menu backgrounds under their names.</summary>
internal sealed class FileMenuSeasonalsStorage(DataPaths paths) : IMenuSeasonalsStorage
{
	/// <inheritdoc />
	public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(FileStorage.List(paths.MenuSeasonals));
	}

	/// <inheritdoc />
	public Task SaveAsync(string name, Stream content, CancellationToken cancellationToken = default)
	{
		return FileStorage.SaveAsync(SafePath.Combine(paths.MenuSeasonals, name), content, cancellationToken);
	}

	/// <inheritdoc />
	public Task<Stream?> OpenAsync(string name, CancellationToken cancellationToken = default)
	{
		return Task.FromResult<Stream?>(FileStorage.Open(SafePath.Combine(paths.MenuSeasonals, name)));
	}

	/// <inheritdoc />
	public Task RenameAsync(string name, string newName, CancellationToken cancellationToken = default)
	{
		File.Move(
			SafePath.Combine(paths.MenuSeasonals, name),
			SafePath.Combine(paths.MenuSeasonals, newName));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
	{
		FileStorage.Delete(SafePath.Combine(paths.MenuSeasonals, name));
		return Task.CompletedTask;
	}
}
