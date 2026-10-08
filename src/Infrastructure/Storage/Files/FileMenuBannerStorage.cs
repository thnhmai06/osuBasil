using System.Collections.Concurrent;
using Basil.Application.Storage.Contracts.Content;

namespace Basil.Infrastructure.Storage.Files;

/// <summary>Holds menu banner images in memory from startup and changes them only through its storage methods.</summary>
internal sealed class FileMenuBannerStorage(DataPaths paths) : IMenuBannerStorage, IResident
{
	private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);

	/// <inheritdoc />
	public async Task LoadAsync(CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(paths.MenuBanners);
		foreach (var path in FileStorage.Files(paths.MenuBanners, "*")!)
			_files[Path.GetFileName(path)] = await File.ReadAllBytesAsync(path, cancellationToken);
	}

	/// <inheritdoc />
	public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult<IReadOnlyList<string>>([.. _files.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)]);
	}

	/// <inheritdoc />
	public async Task SaveAsync(string name, Stream content, CancellationToken cancellationToken = default)
	{
		var path = SafePath.Combine(paths.MenuBanners, name);
		var bytes = await ReadAsync(content, cancellationToken);
		_files[name] = bytes;
		using var storedContent = new MemoryStream(bytes);
		await FileStorage.SaveAsync(path, storedContent, cancellationToken);
	}

	/// <inheritdoc />
	public Task<Stream?> OpenAsync(string name, CancellationToken cancellationToken = default)
	{
		SafePath.Combine(paths.MenuBanners, name);
		return Task.FromResult<Stream?>(_files.TryGetValue(name, out var bytes) ? new MemoryStream(bytes, writable: false) : null);
	}

	/// <inheritdoc />
	public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
	{
		var path = SafePath.Combine(paths.MenuBanners, name);
		_files.TryRemove(name, out _);
		FileStorage.Delete(path);
		return Task.CompletedTask;
	}

	private static async Task<byte[]> ReadAsync(Stream content, CancellationToken cancellationToken)
	{
		using var buffer = new MemoryStream();
		await content.CopyToAsync(buffer, cancellationToken);
		return buffer.ToArray();
	}
}
