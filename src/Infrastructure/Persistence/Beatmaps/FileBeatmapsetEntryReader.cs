using Basil.Application.Persistence.Beatmaps;
using Basil.Domain.Content;

namespace Basil.Infrastructure.Persistence.Beatmaps;

/// <summary>Reads what lies in the beatmapset storage, by top-level name, as it is now.</summary>
internal static class FileBeatmapsetEntryReader
{
	/// <summary>Builds the entry of an archive file lying in the storage.</summary>
	/// <param name="path">The archive's full path.</param>
	/// <param name="name">The archive's name.</param>
	/// <returns>The archive entry.</returns>
	internal static FileBeatmapsetArchive ArchiveAt(string path, string name)
	{
		var info = new FileInfo(path);
		return new FileBeatmapsetArchive(path, name,
			new ContentStamp(info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)));
	}

	/// <summary>Builds an entry for a top-level name, or <see langword="null" /> when it is not an entry.</summary>
	/// <param name="root">The storage directory's full path.</param>
	/// <param name="name">The entry's name in the storage.</param>
	/// <param name="attributesOf">Reads the attributes of a path; the file system's own unless a test replaces it.</param>
	/// <returns>
	///     The entry, or <see langword="null" /> when the name is neither a folder nor an .osz archive. An entry that
	///     vanishes while it is read reads as one that does not exist.
	/// </returns>
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
