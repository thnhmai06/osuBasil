using System.Collections.Concurrent;
using Basil.Application.Storage.Contracts.Content;
using Basil.Infrastructure.Storage.Common;
using Basil.Infrastructure.Storage.Common.Options;
using Basil.Infrastructure.Storage.Common.Queries;

namespace Basil.Infrastructure.Storage.Content;

/// <summary>Holds menu banner images in memory from startup and changes them only through its storage methods.</summary>
internal sealed class FileMenuBannerStorage(DataPaths paths) : IMenuBannerStorage, IResident
{
	// TODO: CHÚ Ý: CÁC FILE ẢNH TRONG MENU BANNERS SẼ ĐƯỢC CẤP THÔNG QUA TRUYỀN TRỰC TIẾP URL TRÊN HOSTS, KHÔNG PHẢI TRUYỀN TRỰC TIẾP ẢNH,
	// VÀ CŨNG KHÔNG NÊN LOAD TOÀN BỘ RESOURCE NÀY VÀO TRONG MEMORY. XEM TODO TRƯỚC ĐÓ.
	private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);

	/// <inheritdoc />
	public Task<IEnumerable<string>> ListAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult<IEnumerable<string>>(
			_files.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
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
		return Task.FromResult<Stream?>(_files.TryGetValue(name, out var bytes)
			? new MemoryStream(bytes, false)
			: null);
	}

	/// <inheritdoc />
	public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
	{
		var path = SafePath.Combine(paths.MenuBanners, name);
		_files.TryRemove(name, out _);
		FileStorage.Delete(path);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task LoadAsync(CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(paths.MenuBanners);
		foreach (var path in FileStorage.List(paths.MenuBanners, "*")!)
			_files[Path.GetFileName(path)] = await File.ReadAllBytesAsync(path, cancellationToken);
	}

	private static async Task<byte[]> ReadAsync(Stream content, CancellationToken cancellationToken)
	{
		using var buffer = new MemoryStream();
		await content.CopyToAsync(buffer, cancellationToken);
		return buffer.ToArray();
	}
}