using Basil.Infrastructure.Common;
using Basil.Infrastructure.Common.Files;
using Microsoft.Extensions.Options;

namespace Basil.Testing.Persistence;

/// <summary>
///     A data directory of its own in the temporary directory, with every storage directory created; deleted when
///     disposed.
/// </summary>
public sealed class TempData : IDisposable
{
	/// <summary>Creates the data directory.</summary>
	/// <param name="createDirectories">Whether to create the storage directories, as the server does at startup.</param>
	public TempData(bool createDirectories = true)
	{
		Root = Path.Combine(Path.GetTempPath(), "basil-test-" + Guid.NewGuid().ToString("N"));
		Paths = new DataPaths(Options.Create(new StorageOptions { DataDirectory = Root }));
		if (createDirectories) Paths.CreateDirectories();
	}

	/// <summary>Gets the data directory.</summary>
	public string Root { get; }

	/// <summary>Gets the locations of the storages in the data directory.</summary>
	public DataPaths Paths { get; }

	/// <summary>Deletes the data directory.</summary>
	public void Dispose()
	{
		if (!Directory.Exists(Root)) return;

		// A refused-but-written file may carry a name Windows keeps for a device (CON, AUX.png), which only the
		// extended-length form can delete.
		Directory.Delete(OperatingSystem.IsWindows() ? @"\\?\" + Root : Root, true);
	}

	/// <summary>Lists every file under a directory, relative to it and with '/' between folders.</summary>
	/// <param name="directory">The directory.</param>
	/// <returns>The relative names, in alphabetical order.</returns>
	public static string[] FilesUnder(string directory)
	{
		return Directory.Exists(directory)
			? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
				.Select(file => Path.GetRelativePath(directory, file).Replace(Path.DirectorySeparatorChar, '/'))
				.Order(StringComparer.Ordinal)
				.ToArray()
			: [];
	}
}