using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Basil.Domain.Content;
using Basil.Shared.Utilities;

namespace Basil.Infrastructure.Shared.Storage;

public sealed class DataStorage : IReadOnlyDictionary<string, FileInfo>
{
	private const int BufferSize = 4096;

	internal DataStorage(string root, string relativePath)
	{
		RelativePath = relativePath.Replace('\\', '/').Trim('/');
		Path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, RelativePath));
	}

	public string Path { get; }

	public string RelativePath { get; }

	public int Count => Keys.Count();

	public IEnumerable<string> Keys
	{
		get
		{
			if (!Directory.Exists(Path)) yield break;
			foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
			{
				var name = System.IO.Path.GetRelativePath(Path, file).Replace('\\', '/');
				// A file placed by hand under a name no reference can refer to is not one of the stored files.
				if (name.EndsWith(".tmp", StringComparison.Ordinal) || !TryResolve(name, out _)) continue;
				yield return name;
			}
		}
	}

	public IEnumerable<FileInfo> Values => Keys.Select(name => new FileInfo(Resolve(name)));

	public FileInfo this[string key] =>
		TryGetValue(key, out var value) ? value : throw new KeyNotFoundException();

	public bool ContainsKey(string key)
	{
		return TryGetValue(key, out _);
	}

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

	public IEnumerator<KeyValuePair<string, FileInfo>> GetEnumerator()
	{
		return Keys.Select(name => new KeyValuePair<string, FileInfo>(name, new FileInfo(Resolve(name))))
			.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	public string Resolve(string name)
	{
		return SafePath.Combine(Path, name);
	}

	public IEnumerable<string> Search(string pattern)
	{
		if (!Directory.Exists(Path)) yield break;
		foreach (var file in Directory.EnumerateFiles(Path, pattern))
			yield return System.IO.Path.GetFileName(file);
	}

	public ContentReference ReferenceOf(string name)
	{
		return new ContentReference(RelativePath + "/" + name);
	}

	public bool TryGetName(ContentReference reference, [NotNullWhen(true)] out string? name)
	{
		var prefix = RelativePath + "/";
		if (!reference.IsOffline || !reference.Value.StartsWith(prefix, StringComparison.Ordinal))
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

	public Task SaveAsync(string name, Stream content, CancellationToken ct)
	{
		return SaveAsync(name, (output, token) => content.CopyToAsync(output, token), ct);
	}

	public async Task SaveAsync(string name, Func<Stream, CancellationToken, Task> write,
		CancellationToken ct)
	{
		var path = Resolve(name);
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
		var temporary = TempFiles.Name(path);

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
				await write(stream, ct);
			}

			File.Move(temporary, path, true);
		}
		finally
		{
			if (File.Exists(temporary)) File.Delete(temporary);
		}
	}

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

	public void Delete(string name)
	{
		if (!TryResolve(name, out var path) || !File.Exists(path)) return;
		File.Delete(path);
	}

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
		if (!PathValidation.IsValidPath(name))
		{
			path = null;
			return false;
		}

		path = Resolve(name);
		return true;
	}
}