using Basil.Infrastructure.Shared.Storage;
using Microsoft.Extensions.Options;

namespace Basil.Testing.Persistence;

public sealed class TempData : IDisposable
{
	public TempData(bool createDirectories = true)
	{
		Root = Path.Combine(Path.GetTempPath(), "basil-test-" + Guid.NewGuid().ToString("N"));
		Paths = new DataPaths(Options.Create(new StorageOptions { Location = Root }));
		if (createDirectories) Paths.CreateDirectories();
	}

	public string Root { get; }

	public DataPaths Paths { get; }

	public void Dispose()
	{
		if (!Directory.Exists(Root)) return;

		// A refused-but-written file may carry a name Windows keeps for a device (CON, AUX.png), which only the
		// extended-length form can delete.
		Directory.Delete(OperatingSystem.IsWindows() ? @"\\?\" + Root : Root, true);
	}

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