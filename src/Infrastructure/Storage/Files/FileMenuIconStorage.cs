using Basil.Application.Storage.Contracts.Content;

namespace Basil.Infrastructure.Storage.Files;

/// <summary>Stores the main-menu icon image as the icon directory's single file.</summary>
internal sealed class FileMenuIconStorage(DataPaths paths) : IMenuIconStorage
{
	/// <summary>Combines the icon image's file name with the icon directory.</summary>
	/// <param name="name">The file name to store the icon under.</param>
	/// <returns>The absolute path of the icon file.</returns>
	private string PathFor(string name)
	{
		return SafePath.Combine(paths.MenuIcon, name);
	}

	/// <summary>Finds the single stored icon file.</summary>
	/// <returns>The file's path, or <see langword="null" /> when no icon is stored.</returns>
	private string? Find()
	{
		return FileStorage.Files(paths.MenuIcon, "*") is { } files ? files.FirstOrDefault() : null;
	}

	/// <inheritdoc />
	public async Task SaveAsync(string name, Stream content, CancellationToken cancellationToken = default)
	{
		foreach (var file in FileStorage.Files(paths.MenuIcon, "*") ?? [])
			File.Delete(file);

		await FileStorage.SaveAsync(PathFor(name), content, cancellationToken);
	}

	/// <inheritdoc />
	public Task<(string Name, Stream Content)?> OpenAsync(CancellationToken cancellationToken = default)
	{
		var path = Find();
		if (path is null)
			return Task.FromResult<(string Name, Stream Content)?>(null);

		var content = FileStorage.Open(path);
		return Task.FromResult<(string Name, Stream Content)?>(content is null ? null : (Path.GetFileName(path), content));
	}

	/// <inheritdoc />
	public Task DeleteAsync(CancellationToken cancellationToken = default)
	{
		foreach (var file in FileStorage.Files(paths.MenuIcon, "*") ?? [])
			FileStorage.Delete(file);

		return Task.CompletedTask;
	}
}
