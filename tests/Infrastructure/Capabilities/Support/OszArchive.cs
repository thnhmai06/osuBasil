using System.IO.Compression;

namespace Basil.Infrastructure.Capabilities.Tests.Support;

/// <summary>Builds the archives the reader tests read, so each test describes its own set.</summary>
internal static class OszArchive
{
	/// <summary>Builds an archive from its entries, in the order given.</summary>
	/// <param name="entries">The name and content of every entry.</param>
	/// <returns>The archive, read from its start.</returns>
	public static MemoryStream Build(params (string Name, byte[] Content)[] entries)
	{
		var archive = new MemoryStream();
		using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, true))
		{
			foreach (var (name, content) in entries)
			{
				var entry = zip.CreateEntry(name);
				using var contentStream = entry.Open();
				contentStream.Write(content);
			}
		}

		archive.Position = 0;
		return archive;
	}

	/// <summary>Reads a sample file that the tests copy beside the test binary.</summary>
	/// <param name="relativePath">The file's path under the assets folder.</param>
	/// <returns>The file's content.</returns>
	public static byte[] Sample(string relativePath)
	{
		return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", relativePath));
	}
}