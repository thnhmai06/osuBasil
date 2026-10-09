using Basil.Application.Storage.Contracts.Content;
using Basil.Infrastructure.Storage.Common;
using Basil.Infrastructure.Storage.Common.Options;
using Basil.Infrastructure.Storage.Common.Queries;

namespace Basil.Infrastructure.Storage.Files;

/// <summary>Holds the main-menu icon in memory from startup and changes it only through its storage methods.</summary>
internal sealed class FileMenuIconStorage(DataPaths paths) : IMenuIconStorage, IResident
{
	private (string Name, byte[] Content)? _icon;

	/// <inheritdoc />
	public async Task SaveAsync(string name, Stream content, CancellationToken cancellationToken = default)
	{
		var path = SafePath.Combine(paths.MenuIcon, name);
		using var buffer = new MemoryStream();
		await content.CopyToAsync(buffer, cancellationToken);
		var bytes = buffer.ToArray();
		_icon = (name, bytes);
		using var storedContent = new MemoryStream(bytes);
		await FileStorage.SaveAsync(path, storedContent, cancellationToken);
		FileStorage.DeleteExcept(paths.MenuIcon, "*", path);
	}

	/// <inheritdoc />
	public Task<(string Name, Stream Content)?> OpenAsync(CancellationToken cancellationToken = default)
	{
		if (_icon is not { } icon)
			return Task.FromResult<(string Name, Stream Content)?>(null);

		return Task.FromResult<(string Name, Stream Content)?>((icon.Name, new MemoryStream(icon.Content, false)));
	}

	/// <inheritdoc />
	public Task DeleteAsync(CancellationToken cancellationToken = default)
	{
		_icon = null;
		foreach (var file in FileStorage.Files(paths.MenuIcon, "*") ?? [])
			FileStorage.Delete(file);

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task LoadAsync(CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(paths.MenuIcon);
		if (FileStorage.Files(paths.MenuIcon, "*")?.FirstOrDefault() is { } path)
			_icon = (Path.GetFileName(path), await File.ReadAllBytesAsync(path, cancellationToken));
	}
}