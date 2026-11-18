using Basil.Application.Beatmaps.Persistence;
using Basil.Domain.Content;

namespace Basil.Infrastructure.Beatmaps.Storage;

internal static class FileBeatmapsetEntryReader
{
	internal static FileBeatmapsetArchive ArchiveAt(string path, string name)
	{
		var info = new FileInfo(path);
		return new FileBeatmapsetArchive(path, name,
			new ContentStamp(info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)));
	}

	internal static BeatmapsetSource? EntryOf(string root, string name,
		Func<string, FileAttributes>? attributesOf = null)
	{
		var path = Path.Combine(root, name);
		try
		{
			// A linked entry points outside the storage; it is never taken in, changed or deleted.
			if ((File.Exists(path) || Directory.Exists(path)) &&
			    (attributesOf ?? File.GetAttributes)(path).HasFlag(FileAttributes.ReparsePoint))
				return null;

			if (Directory.Exists(path))
			{
				var archivePath = Path.Combine(path, FileBeatmapsetNames.ArchiveName);
				var archive = File.Exists(archivePath) ? ArchiveAt(archivePath, FileBeatmapsetNames.ArchiveName) : null;
				return new FileBeatmapsetFolder(path, name, FileBeatmapsetNames.LeadingId(name), true, archive);
			}

			// Only an archive is a source; any other file placed here, such as a loose difficulty or a .zip, is not.
			if (File.Exists(path)) return FileBeatmapsetNames.IsArchiveName(name) ? ArchiveAt(path, name) : null;
		}
		catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
		{
			// It vanished while it was read: it reads as not existing, as when it was gone before.
		}

		return FileBeatmapsetNames.IsArchiveName(name)
			? null
			: new FileBeatmapsetFolder(path, name, FileBeatmapsetNames.LeadingId(name), false, null);
	}
}