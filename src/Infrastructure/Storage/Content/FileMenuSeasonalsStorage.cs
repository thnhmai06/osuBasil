using System.Collections.Concurrent;
using Basil.Application.Storage.Contracts.Content;
using Basil.Infrastructure.Storage.Common;
using Basil.Infrastructure.Storage.Common.Options;
using Basil.Infrastructure.Storage.Common.Queries;

namespace Basil.Infrastructure.Storage.Content;

/// <summary>Holds seasonal menu backgrounds in memory from startup and changes them only through its storage methods.</summary>
internal sealed class FileMenuSeasonalsStorage(DataPaths paths) : IMenuSeasonalsStorage, IResident
{
	private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);

	/// <inheritdoc />
	public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult<IReadOnlyList<string>>([
			.. _files.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
		]);
	}

	/// <inheritdoc />
	public async Task SaveAsync(string name, Stream content, CancellationToken cancellationToken = default)
	{
		var path = SafePath.Combine(paths.MenuSeasonals, name);
		var bytes = await ReadAsync(content, cancellationToken);
		_files[name] = bytes;
		using var storedContent = new MemoryStream(bytes);
		await FileStorage.SaveAsync(path, storedContent, cancellationToken);
	}

	/// <inheritdoc />
	public Task<Stream?> OpenAsync(string name, CancellationToken cancellationToken = default)
	{
		SafePath.Combine(paths.MenuSeasonals, name);
		return Task.FromResult<Stream?>(_files.TryGetValue(name, out var bytes)
			? new MemoryStream(bytes, false)
			: null);
	}

	/// <inheritdoc />
	public Task RenameAsync(string name, string newName, CancellationToken cancellationToken = default)
	{
		var path = SafePath.Combine(paths.MenuSeasonals, name);
		var newPath = SafePath.Combine(paths.MenuSeasonals, newName);
		if (_files.ContainsKey(newName) || !_files.TryRemove(name, out var bytes))
			throw new IOException("The source file does not exist or the destination file already exists.");
		if (!_files.TryAdd(newName, bytes))
		{
			_files.TryAdd(name, bytes);
			throw new IOException("The destination file already exists.");
		}

		File.Move(path, newPath);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
	{
		var path = SafePath.Combine(paths.MenuSeasonals, name);
		_files.TryRemove(name, out _);
		FileStorage.Delete(path);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task LoadAsync(CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(paths.MenuSeasonals);
		foreach (var path in FileStorage.List(paths.MenuSeasonals, "*")!)
			_files[Path.GetFileName(path)] = await File.ReadAllBytesAsync(path, cancellationToken);
	}

	private static async Task<byte[]> ReadAsync(Stream content, CancellationToken cancellationToken)
	{
		using var buffer = new MemoryStream();
		await content.CopyToAsync(buffer, cancellationToken);
		return buffer.ToArray();
	}
}