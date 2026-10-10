using System.Collections.Concurrent;
using Basil.Application.Storage.Contracts.Content;
using Basil.Infrastructure.Storage.Common;
using Basil.Infrastructure.Storage.Common.Options;
using Basil.Infrastructure.Storage.Common.Queries;

namespace Basil.Infrastructure.Storage.Content;

/// <summary>Holds FAQ entries in memory from startup and changes them only through its storage methods.</summary>
internal sealed class FileFaqStorage(DataPaths paths) : IFaqStorage, IResident
{
	// TODO: Trên các storage mà nơi lưu trữ (source of truth) phụ thuộc vào bên ngoài, KHÔNG ĐƯỢC LOAD HẾT TẤT CẢ VÀO MEMORY LUÔN.
	// Trong Memory chỉ lưu "tham chiếu" tới nó, tức ám chỉ là đường dẫn tới nó, KHÔNG PHẢI LÀ LƯU TOÀN BỘ NỘI DUNG
	// CỦA FILE VÀO TRONG MEMORY NÀY. Các File*Storage khác cũng đều bị dính lỗi tương tự như này.
	private readonly ConcurrentDictionary<string, byte[]> _entries = new(StringComparer.Ordinal);

	/// <inheritdoc />
	public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult<IReadOnlyList<string>>([
			.. _entries.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
		]);
	}

	/// <inheritdoc />
	public async Task SaveAsync(string entry, Stream content, CancellationToken cancellationToken = default)
	{
		var path = PathFor(paths.Faqs, entry);
		var bytes = await ReadAsync(content, cancellationToken);
		_entries[entry] = bytes;
		using var storedContent = new MemoryStream(bytes);
		await FileStorage.SaveAsync(path, storedContent, cancellationToken);
	}

	/// <inheritdoc />
	public Task<Stream?> OpenAsync(string entry, CancellationToken cancellationToken = default)
	{
		PathFor(paths.Faqs, entry);
		return Task.FromResult<Stream?>(_entries.TryGetValue(entry, out var bytes)
			? new MemoryStream(bytes, false)
			: null);
	}

	/// <inheritdoc />
	public Task DeleteAsync(string entry, CancellationToken cancellationToken = default)
	{
		var path = PathFor(paths.Faqs, entry);
		_entries.TryRemove(entry, out _);
		FileStorage.Delete(path);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task LoadAsync(CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(paths.Faqs);
		foreach (var path in Directory.EnumerateFiles(paths.Faqs, "*.txt", SearchOption.AllDirectories))
		{
			var relative = Path.GetRelativePath(paths.Faqs, path);
			var entry = Path.ChangeExtension(relative, null)
				.Replace(Path.DirectorySeparatorChar, ':')
				.Replace(Path.AltDirectorySeparatorChar, ':');
			_entries[entry] = await File.ReadAllBytesAsync(path, cancellationToken);
		}
	}

	/// <summary>Resolves the path of a FAQ entry's file.</summary>
	/// <param name="root">The directory the entry files belong to.</param>
	/// <param name="entry">The name of the entry; its <c>:</c> segments name the subdirectories.</param>
	/// <returns>The absolute path of the entry's <c>.txt</c> file.</returns>
	/// <exception cref="ArgumentException">The <paramref name="entry" /> names an unsafe path.</exception>
	private static string PathFor(string root, string entry)
	{
		return SafePath.Combine(root, string.Join('/', entry.Split(':')) + ".txt");
	}

	private static async Task<byte[]> ReadAsync(Stream content, CancellationToken cancellationToken)
	{
		using var buffer = new MemoryStream();
		await content.CopyToAsync(buffer, cancellationToken);
		return buffer.ToArray();
	}
}