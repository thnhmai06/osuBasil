using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Basil.Domain.Content;

namespace Basil.Infrastructure.Persistence.Files;

/// <summary>The files stored in one directory of the data directory, by name, and the asset references that refer to them.</summary>
/// <remarks>Names are relative to the directory, with '/' between folders. Reading it reads the disk as it is now.</remarks>
internal sealed class DataStorage : IReadOnlyDictionary<string, FileInfo>
{
	private const int BufferSize = 4096;

	/// <summary>Initializes the files of one directory of the data directory.</summary>
	/// <param name="root">The data directory every stored file belongs to.</param>
	/// <param name="relativePath">The directory's path, relative to the data directory.</param>
	internal DataStorage(string root, string relativePath)
	{
		RelativePath = relativePath.Replace('\\', '/').Trim('/');
		Path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, RelativePath));
	}

	/// <summary>Gets the absolute path of the directory.</summary>
	public string Path { get; }

	/// <summary>Gets the directory's path relative to the data directory, with '/' between folders.</summary>
	public string RelativePath { get; }

	/// <inheritdoc />
	public int Count => Keys.Count();

	/// <inheritdoc />
	public IEnumerable<string> Keys
	{
		get
		{
			if (!Directory.Exists(Path)) yield break;
			foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
			{
				var name = System.IO.Path.GetRelativePath(Path, file).Replace('\\', '/');
				if (name.EndsWith(".tmp", StringComparison.Ordinal)) continue;
				yield return name;
			}
		}
	}

	/// <inheritdoc />
	public IEnumerable<FileInfo> Values => Keys.Select(name => new FileInfo(Resolve(name)));

	/// <inheritdoc />
	public FileInfo this[string key] =>
		TryGetValue(key, out var value) ? value : throw new KeyNotFoundException();

	/// <inheritdoc />
	public bool ContainsKey(string key)
	{
		return TryGetValue(key, out _);
	}

	/// <inheritdoc />
	public bool TryGetValue(string key, [NotNullWhen(true)] out FileInfo? value)
	{
		if (!TryResolve(key, out var path) || !File.Exists(path))
		{
			value = null;
			return false;
		}

		value = new FileInfo(path);
		return true;
	}

	/// <inheritdoc />
	public IEnumerator<KeyValuePair<string, FileInfo>> GetEnumerator()
	{
		return Keys.Select(name => new KeyValuePair<string, FileInfo>(name, new FileInfo(Resolve(name))))
			.GetEnumerator();
	}

	/// <inheritdoc />
	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	/// <summary>Combines a file's name with the directory.</summary>
	/// <param name="name">The file's name, relative to the directory.</param>
	/// <returns>The absolute path of the file.</returns>
	/// <exception cref="ArgumentException"><paramref name="name" /> is not a safe file name.</exception>
	public string Resolve(string name)
	{
		return SafePath.Combine(Path, name);
	}

	/// <summary>Lists the names of the files directly in the directory matching a search pattern.</summary>
	/// <param name="pattern">The OS search pattern the file names must match.</param>
	/// <returns>The file names, or nothing when the directory does not exist.</returns>
	public IEnumerable<string> Search(string pattern)
	{
		if (!Directory.Exists(Path)) yield break;
		foreach (var file in Directory.EnumerateFiles(Path, pattern))
			yield return System.IO.Path.GetFileName(file);
	}

	/// <summary>Makes an asset reference to a file of the directory.</summary>
	/// <param name="name">The file's name, relative to the directory.</param>
	/// <returns>The reference to the file.</returns>
	public ContentReference ReferenceOf(string name)
	{
		return new ContentReference(RelativePath + "/" + name);
	}

	/// <summary>Takes the name of a file of the directory out of an asset reference.</summary>
	/// <param name="reference">The reference to take the name from.</param>
	/// <param name="name">The file's name, relative to the directory.</param>
	/// <returns>
	///     <see langword="true" /> when the reference names a safe file name of the directory; otherwise,
	///     <see langword="false" />.
	/// </returns>
	public bool TryGetName(ContentReference reference, [NotNullWhen(true)] out string? name)
	{
		var prefix = RelativePath + "/";
		if (!reference.IsStored || !reference.Value.StartsWith(prefix, StringComparison.Ordinal))
		{
			name = null;
			return false;
		}

		var rest = reference.Value[prefix.Length..];
		if (!TryResolve(rest, out _))
		{
			name = null;
			return false;
		}

		name = rest;
		return true;
	}

	/// <summary>Stores the bytes of a stream as a file, replacing the file already there.</summary>
	/// <param name="name">The name to store the file under.</param>
	/// <param name="content">The bytes to store; the stream is read to its end.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>A save that does not complete leaves the previous file.</remarks>
	public async Task SaveAsync(string name, Stream content, CancellationToken cancellationToken)
	{
		var path = Resolve(name);
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
		var temporary = path + "." + System.IO.Path.GetRandomFileName() + ".tmp";

		try
		{
			await using (var stream = new FileStream(temporary, new FileStreamOptions
			             {
				             Mode = FileMode.CreateNew,
				             Access = FileAccess.Write,
				             Share = FileShare.None,
				             Options = FileOptions.Asynchronous,
				             BufferSize = BufferSize
			             }))
			{
				await content.CopyToAsync(stream, cancellationToken);
			}

			File.Move(temporary, path, true);
		}
		finally
		{
			if (File.Exists(temporary)) File.Delete(temporary);
		}
	}

	/// <summary>Opens a stored file for reading.</summary>
	/// <param name="name">The name of the file to open.</param>
	/// <returns>
	///     A read-only stream over the file, or <see langword="null" /> when the name is unsafe or the file does not
	///     exist.
	/// </returns>
	public FileStream? Open(string name)
	{
		if (!TryResolve(name, out var path) || !File.Exists(path)) return null;
		return new FileStream(path, new FileStreamOptions
		{
			Mode = FileMode.Open,
			Access = FileAccess.Read,
			Share = FileShare.Read,
			Options = FileOptions.Asynchronous,
			BufferSize = BufferSize
		});
	}

	/// <summary>Renames a stored file.</summary>
	/// <param name="name">The current name of the file.</param>
	/// <param name="newName">The new name for the file.</param>
	/// <returns>
	///     <see langword="true" /> when the file was renamed; <see langword="false" /> when it does not exist or the new
	///     name is taken.
	/// </returns>
	/// <exception cref="ArgumentException"><paramref name="newName" /> is not a safe file name.</exception>
	public bool Move(string name, string newName)
	{
		var newPath = Resolve(newName);
		if (!TryResolve(name, out var path) || !File.Exists(path) || File.Exists(newPath)) return false;
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(newPath)!);
		try
		{
			File.Move(path, newPath, false);
			return true;
		}
		catch (IOException)
		{
			return false;
		}
	}

	/// <summary>Deletes a stored file.</summary>
	/// <param name="name">The name of the file to delete.</param>
	/// <remarks>Deleting a file that does not exist does nothing.</remarks>
	public void Delete(string name)
	{
		if (!TryResolve(name, out var path) || !File.Exists(path)) return;
		File.Delete(path);
	}

	/// <summary>Deletes every stored file except one.</summary>
	/// <param name="name">The name of the file to keep; <see langword="null" /> deletes every file.</param>
	public void DeleteAllExcept(string? name)
	{
		foreach (var key in Keys.ToList())
		{
			if (key == name) continue;
			if (TryResolve(key, out var path)) File.Delete(path);
		}
	}

	private bool TryResolve(string name, [NotNullWhen(true)] out string? path)
	{
		try
		{
			path = Resolve(name);
			return true;
		}
		catch (ArgumentException)
		{
			path = null;
			return false;
		}
	}
}