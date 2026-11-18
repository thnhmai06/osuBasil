using System.IO.Compression;

namespace Basil.Testing.Persistence;

public static class OszArchive
{
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

	public static byte[] Sample(string relativePath)
	{
		return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", relativePath));
	}
}