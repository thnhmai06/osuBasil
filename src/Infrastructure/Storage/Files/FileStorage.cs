namespace Basil.Infrastructure.Storage.Files;

/// <summary>Stores single files for the file storages.</summary>
internal static class FileStorage
{
	private const int BufferSize = 4096;

	/// <summary>Stores the bytes of a stream as a file, replacing the file already there.</summary>
	/// <param name="path">The path of the file.</param>
	/// <param name="content">The bytes to store; the stream is read to its end.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>A stored file is replaced whole: a write that does not complete leaves the previous bytes.</remarks>
	public static async Task SaveAsync(string path, Stream content, CancellationToken cancellationToken)
	{
		var directory = Path.GetDirectoryName(path)!;
		Directory.CreateDirectory(directory);
		var temporary = Path.Combine(directory, Path.GetRandomFileName());

		try
		{
			await using (var stream = Write(temporary))
			{
				await content.CopyToAsync(stream, cancellationToken);
			}

			File.Move(temporary, path, true);
		}
		finally
		{
			File.Delete(temporary);
		}
	}

	/// <summary>Opens a stored file.</summary>
	/// <param name="path">The path of the file.</param>
	/// <returns>A read-only stream over the file, or <see langword="null" /> when the file does not exist.</returns>
	public static FileStream? Open(string path)
	{
		return File.Exists(path)
			? new FileStream(path, new FileStreamOptions
			{
				Mode = FileMode.Open,
				Access = FileAccess.Read,
				Share = FileShare.Read,
				Options = FileOptions.Asynchronous,
				BufferSize = BufferSize
			})
			: null;
	}

	/// <summary>Creates a stream for writing a stored file exclusively and asynchronously.</summary>
	/// <param name="path">The path of the file to create.</param>
	/// <returns>The open write stream.</returns>
	private static FileStream Write(string path)
	{
		return new FileStream(path, new FileStreamOptions
		{
			Mode = FileMode.Create,
			Access = FileAccess.Write,
			Share = FileShare.None,
			Options = FileOptions.Asynchronous,
			BufferSize = BufferSize
		});
	}

	/// <summary>Lists the paths of the files of a directory.</summary>
	/// <param name="directory">The path of the directory.</param>
	/// <param name="pattern">The search pattern the file names must match.</param>
	/// <returns>The file paths, or <see langword="null" /> when the directory does not exist.</returns>
	public static IEnumerable<string>? Files(string directory, string pattern)
	{
		return Directory.Exists(directory)
			? Directory.EnumerateFiles(directory, pattern)
			: null;
	}

	/// <summary>Deletes every file of a directory except one.</summary>
	/// <param name="directory">The path of the directory.</param>
	/// <param name="pattern">The search pattern the file names must match.</param>
	/// <param name="kept">The path of the file kept.</param>
	public static void DeleteExcept(string directory, string pattern, string kept)
	{
		var files = Files(directory, pattern);
		if (files is null)
			return;

		foreach (var file in files.Where(file => Path.GetFullPath(file) != Path.GetFullPath(kept)))
			File.Delete(file);
	}

	/// <summary>Deletes a stored file.</summary>
	/// <param name="path">The path of the file.</param>
	/// <remarks>Deleting a missing file does nothing.</remarks>
	public static void Delete(string path)
	{
		if (File.Exists(path))
			File.Delete(path);
	}

	/// <summary>Lists the names of the files of a directory, sorted.</summary>
	/// <param name="directory">The path of the directory.</param>
	/// <returns>The names of the files the directory holds.</returns>
	public static IReadOnlyList<string> List(string directory)
	{
		Directory.CreateDirectory(directory);

		return
		[
			.. Directory.EnumerateFiles(directory)
				.Select(file => Path.GetFileName(file))
				.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
		];
	}
}