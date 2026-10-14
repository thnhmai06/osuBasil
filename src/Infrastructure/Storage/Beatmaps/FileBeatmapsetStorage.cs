using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Storage.Common.Options;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Storage.Beatmaps;

/// <summary>Stores each beatmapset's archive and the files made from it in its own folder of Data/Beatmapsets, and takes in what is placed there.</summary>
internal sealed class FileBeatmapsetStorage(
	DataPaths paths,
	TimeProvider time,
	ILogger<FileBeatmapsetStorage> logger) : IBeatmapsetStorage
{
	/// <summary>
	///     The marker a top-level name carries while its folder is being removed from the storage; such a name is never
	///     a set, an entry, or watched.
	/// </summary>
	internal const string DeletedMarker = ".deleted_";

	/// <summary>
	///     The marker a top-level name starts with while it is being written: an upload, a file or a set folder before
	///     it is moved into place. Such a name is never a set, an entry, or watched.
	/// </summary>
	internal const string IncomingMarker = ".incoming_";

	/// <summary>The most files one upload may hold, uploaded and unpacked together.</summary>
	private const int MaxUploadFiles = 20_000;

	/// <summary>The most bytes one upload may hold, uploaded and unpacked together.</summary>
	private const long MaxUploadBytes = 4L * 1024 * 1024 * 1024;

	/// <summary>How deep archives of beatmapsets may lie inside one another in an upload.</summary>
	private const int MaxUploadDepth = 3;

	/// <summary>How many folders deep a file of an upload, or of an archive in it, may lie.</summary>
	private const int MaxPathSegments = 32;

	private const string VersionFolder = "versions";
	private const string BackgroundFolder = "backgrounds";
	private const string AudioFolder = "audio";
	private const string VideoFolder = "video";

	/// <summary>The name of a set's archive, stored byte for byte as it was given.</summary>
	internal const string ArchiveName = "archive.osz";

	/// <summary>The name of the archive built from <see cref="ArchiveName" /> without its video entries.</summary>
	internal const string ArchiveWithoutVideoName = "archive-novideo.osz";

	/// <summary>The name of the set's storyboard file, stored in the set folder itself.</summary>
	internal const string StoryboardName = "storyboard.osb";

	/// <summary>The prefix of an audio preview file, stored in the audio folder.</summary>
	internal const string PreviewPrefix = "preview_";

	private static readonly FrozenSet<string> VideoExtensions =
		new[] { ".mp4", ".avi", ".flv", ".m4v", ".mkv", ".mov", ".mpg", ".mpeg", ".webm", ".wmv" }.ToFrozenSet(
			StringComparer.OrdinalIgnoreCase);

	private static readonly FrozenSet<char> InvalidNameChars = "\"<>:/\\|?*".ToFrozenSet();

	/// <summary>The names Windows keeps for devices, which no stored file or folder may take, whatever its extension.</summary>
	private static readonly FrozenSet<string> ReservedNames = new[]
	{
		"CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7",
		"COM8", "COM9", "COM¹", "COM²", "COM³", "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8",
		"LPT9", "LPT¹", "LPT²", "LPT³"
	}.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	private static readonly ConcurrentDictionary<int, SemaphoreSlim> SetLocks = new();

	/// <inheritdoc />
	public async Task<BeatmapsetUpload> SaveAsync(IReadOnlyList<BeatmapsetUploadFile> files,
		CancellationToken cancellationToken = default)
	{
		var upload = new FileBeatmapsetUpload(Incoming());
		var filesRoot = Path.Combine(upload.Path, "files");
		var packedRoot = Path.Combine(upload.Path, "packed");
		Directory.CreateDirectory(filesRoot);
		Directory.CreateDirectory(packedRoot);
		try
		{
			var budget = new UploadBudget();
			foreach (var file in files)
			{
				var path = PathInside(filesRoot, file.Path) ??
				           throw new ArgumentException($"The path '{file.Path}' is not a safe relative path.",
					           nameof(files));
				if (File.Exists(path) || Directory.Exists(path))
					throw new ArgumentException($"The path '{file.Path}' is given twice.", nameof(files));

				Directory.CreateDirectory(Path.GetDirectoryName(path)!);
				await WriteAsync(file.Content, path, budget, cancellationToken).ConfigureAwait(false);
			}

			var finder = new SetFinder(
				async (folder, name) =>
				{
					var packedFiles = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
					// The packed copy is counted before it is written: it is about as large as the files it packs.
					budget.AddBytes(packedFiles.Sum(file => new FileInfo(file).Length));
					var packed = Path.Combine(packedRoot, $"{Guid.NewGuid():N}.osz");
					await PackFilesAsync(folder, packedFiles, packed, cancellationToken).ConfigureAwait(false);
					upload.MutableArchives.Add(ArchiveAt(packed, name));
				},
				(archive, name) =>
				{
					upload.MutableArchives.Add(ArchiveAt(archive, name));
					return Task.CompletedTask;
				},
				upload.MutableRejected.Add);
			await FindAsync(filesRoot, filesRoot, budget, 0, finder, cancellationToken).ConfigureAwait(false);
			return upload;
		}
		catch
		{
			DeleteUpload(upload);
			throw;
		}
	}

	/// <inheritdoc />
	public Task DeleteAsync(BeatmapsetUpload upload, CancellationToken cancellationToken = default)
	{
		if (upload is not FileBeatmapsetUpload held)
			throw new ArgumentException("The upload does not belong to this storage.", nameof(upload));

		DeleteUpload(held);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task DeleteAsync(BeatmapsetArchiveEntry packed, CancellationToken cancellationToken = default)
	{
		if (packed is not FileBeatmapsetArchive { PackedFrom: not null } archive)
			throw new ArgumentException("The archive was not packed by this storage.", nameof(packed));

		try
		{
			DeleteFileIfExists(archive.Path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			logger.LogWarning(exception, "Could not remove the packed archive {Path}", archive.Path);
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<BeatmapsetEntry>> UnpackAsync(BeatmapsetCollectionEntry collection,
		CancellationToken cancellationToken = default)
	{
		if (collection is not FileBeatmapsetCollection placed)
			throw new ArgumentException("The collection does not belong to this storage.", nameof(collection));

		var laidOut = new List<string>();
		var finder = new SetFinder(MoveOut, MoveOut, static _ => { });
		string? unpacked = null;
		try
		{
			if (Directory.Exists(placed.Path))
			{
				await FindAsync(placed.Path, placed.Path, new UploadBudget(), 0, finder, cancellationToken)
					.ConfigureAwait(false);
				RemoveEmptyFolders(placed.Path);
			}
			else if (File.Exists(placed.Path))
			{
				unpacked = Incoming();
				await UnpackAsync(placed.Path, unpacked, new UploadBudget(), cancellationToken).ConfigureAwait(false);
				await FindAsync(unpacked, unpacked, new UploadBudget(), 1, finder, cancellationToken)
					.ConfigureAwait(false);
				File.Delete(placed.Path);
			}
		}
		catch (Exception exception) when (exception is InvalidDataException or IOException
			                                  or UnauthorizedAccessException)
		{
			logger.LogWarning(exception, "Could not unpack the beatmapset collection {Name}", placed.Name);
		}
		finally
		{
			if (unpacked is not null) DeleteUpload(new FileBeatmapsetUpload(unpacked));
		}

		return laidOut.Select(EntryOf).OfType<BeatmapsetEntry>().ToList();

		// A beatmapset found in the collection moves to the top of the storage, under a name no other entry has.
		Task MoveOut(string path, string _)
		{
			var name = TopLevelName(Path.GetFileName(path));
			var target = Path.Combine(paths.Beatmapsets.Path, name);
			if (Directory.Exists(path)) Directory.Move(path, target);
			else File.Move(path, target);
			laidOut.Add(name);
			return Task.CompletedTask;
		}
	}

	/// <summary>Looks through one folder for beatmapsets, handing each one found to a finder.</summary>
	/// <param name="folder">The folder to look through.</param>
	/// <param name="root">The folder the search started from, which names are relative to.</param>
	/// <param name="budget">What may still be unpacked.</param>
	/// <param name="depth">How many archives the folder lies in.</param>
	/// <param name="finder">What to do with each beatmapset found.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>
	///     A folder with difficulties directly in it is one beatmapset, except the root, whose difficulties lie loose. An
	///     archive that holds beatmapsets is unpacked in its place and looked through; any other archive is one beatmapset.
	/// </remarks>
	private static async Task FindAsync(string folder, string root, UploadBudget budget, int depth, SetFinder finder,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var difficulties = Directory.GetFiles(folder, "*.osu");
		if (difficulties.Length > 0)
		{
			if (folder != root)
			{
				await finder.Folder(folder, NameIn(root, folder)).ConfigureAwait(false);
				return;
			}

			foreach (var file in difficulties) finder.Loose(NameIn(root, file));
		}

		// Taken before archives are unpacked here, so an unpacked archive is looked through once.
		var folders = Directory.GetDirectories(folder).Order(StringComparer.Ordinal).ToList();
		foreach (var file in Directory.GetFiles(folder).Order(StringComparer.Ordinal))
		{
			if (!IsArchiveName(file)) continue;

			if (!HoldsBeatmapsets(file))
			{
				await finder.Archive(file, NameIn(root, file)).ConfigureAwait(false);
				continue;
			}

			if (depth >= MaxUploadDepth)
				throw new InvalidDataException("Archives are nested too deeply.");

			// The archive's place is taken by a folder of what it holds, so what is found in it is named after it.
			var unpacked = file + IncomingMarker;
			await UnpackAsync(file, unpacked, budget, cancellationToken).ConfigureAwait(false);
			File.Delete(file);
			Directory.Move(unpacked, file);
			await FindAsync(file, root, budget, depth + 1, finder, cancellationToken).ConfigureAwait(false);
		}

		foreach (var child in folders)
			await FindAsync(child, root, budget, depth, finder, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Builds the name of a path relative to the folder a search started from.</summary>
	/// <param name="root">The folder the search started from.</param>
	/// <param name="path">A path inside it.</param>
	/// <returns>The relative path, its folders separated by <c>/</c>.</returns>
	private static string NameIn(string root, string path)
	{
		return Path.GetRelativePath(root, path).Replace('\\', '/');
	}

	/// <summary>Determines whether a name is that of an archive that can hold beatmapsets.</summary>
	/// <param name="name">The file name or path.</param>
	/// <returns><see langword="true" /> for an .osz or a .zip archive.</returns>
	private static bool IsArchiveName(string name)
	{
		return name.EndsWith(".osz", StringComparison.OrdinalIgnoreCase) ||
		       name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
	}


	/// <summary>Removes a folder and the folders below it that hold no file, keeping every folder that still holds one.</summary>
	/// <param name="folder">The folder's full path.</param>
	/// <returns><see langword="true" /> when the folder itself was removed.</returns>
	private static bool RemoveEmptyFolders(string folder)
	{
		var empty = true;
		foreach (var child in Directory.GetDirectories(folder))
			if (new DirectoryInfo(child).Attributes.HasFlag(FileAttributes.ReparsePoint) || !RemoveEmptyFolders(child))
				empty = false;

		if (!empty || Directory.EnumerateFiles(folder).Any()) return false;
		Directory.Delete(folder);
		return true;
	}
	/// <summary>Determines whether a folder placed by hand holds several beatmapsets rather than being one.</summary>
	/// <param name="folder">The folder's full path.</param>
	/// <returns>
	///     <see langword="true" /> when it is no set's folder, has no difficulty directly in it, and holds an archive or a
	///     difficulty in a folder below it.
	/// </returns>
	private static bool IsCollection(string folder)
	{
		if (IsSetFolder(folder) || Directory.EnumerateFiles(folder, "*.osu").Any()) return false;

		var options = new EnumerationOptions
		{
			RecurseSubdirectories = true,
			AttributesToSkip = FileAttributes.ReparsePoint,
			MaxRecursionDepth = MaxPathSegments
		};
		return Directory.EnumerateFiles(folder, "*", options)
			.Any(file => IsArchiveName(file) || file.EndsWith(".osu", StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>Builds a name for an entry moved to the top of the storage that no entry has yet.</summary>
	/// <param name="name">The name the entry had.</param>
	/// <returns>The name, with a number added when it is taken, and made safe when it is reserved or hidden.</returns>
	private string TopLevelName(string name)
	{
		if (IsReservedName(name) || IsHidden(name)) name = "_" + name;

		var stem = Path.GetFileNameWithoutExtension(name);
		var extension = Path.GetExtension(name);
		var candidate = name;
		for (var number = 2;
		     File.Exists(Path.Combine(paths.Beatmapsets.Path, candidate)) ||
		     Directory.Exists(Path.Combine(paths.Beatmapsets.Path, candidate));
		     number++)
			candidate = $"{stem} ({number}){extension}";

		return candidate;
	}

	/// <summary>Determines whether a file name is one Windows keeps for a device, whatever its extension.</summary>
	/// <param name="name">The file name.</param>
	/// <returns><see langword="true" /> for names such as <c>CON</c>, <c>nul.txt</c> or <c>COM1</c>.</returns>
	private static bool IsReservedName(string name)
	{
		var dot = name.IndexOf('.');
		return ReservedNames.Contains((dot < 0 ? name : name[..dot]).TrimEnd(' '));
	}

	/// <summary>What a search for beatmapsets does with each one it finds.</summary>
	/// <param name="Folder">Takes a folder of one beatmapset and its name.</param>
	/// <param name="Archive">Takes an archive of one beatmapset and its name.</param>
	/// <param name="Loose">Takes the name of a difficulty lying loose at the root of the search.</param>
	private readonly record struct SetFinder(
		Func<string, string, Task> Folder,
		Func<string, string, Task> Archive,
		Action<string> Loose);

	/// <summary>Determines whether an archive holds beatmapsets rather than being one.</summary>
	/// <param name="file">The archive's full path.</param>
	/// <returns>
	///     <see langword="true" /> when it opens, has no difficulty at its top, and holds a difficulty in a folder or
	///     another archive; otherwise <see langword="false" />, and the archive is taken as one beatmapset.
	/// </returns>
	private static bool HoldsBeatmapsets(string file)
	{
		try
		{
			using var zip = ZipFile.OpenRead(file);
			var names = zip.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToList();
			if (names.Any(entry => !entry.Contains('/') && entry.EndsWith(".osu", StringComparison.OrdinalIgnoreCase)))
				return false;

			return names.Any(entry => entry.EndsWith(".osu", StringComparison.OrdinalIgnoreCase) ||
			                          entry.EndsWith(".osz", StringComparison.OrdinalIgnoreCase) ||
			                          entry.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
		}
		catch (InvalidDataException)
		{
			return false;
		}
	}

	/// <summary>Unpacks an archive of an upload into a folder, leaving out entries whose names are not safe.</summary>
	/// <param name="file">The archive's full path.</param>
	/// <param name="folder">The folder to unpack into, which is created.</param>
	/// <param name="budget">What the upload may still unpack.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	private static async Task UnpackAsync(string file, string folder, UploadBudget budget,
		CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(folder);
		using var zip = ZipFile.OpenRead(file);
		foreach (var entry in zip.Entries)
		{
			if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
			var path = PathInside(folder, entry.FullName);
			if (path is null) continue;

			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			if (File.Exists(path)) File.Delete(path);
			var input = entry.Open();
			await using (input.ConfigureAwait(false))
			{
				await WriteAsync(input, path, budget, cancellationToken).ConfigureAwait(false);
			}
		}
	}

	/// <summary>Writes one file of an upload, counting it against what the upload may hold.</summary>
	/// <param name="content">The content, read to its end.</param>
	/// <param name="path">The file's full path, which must not exist.</param>
	/// <param name="budget">What the upload may still hold.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <exception cref="InvalidDataException">The upload would hold more files or bytes than it may.</exception>
	private static async Task WriteAsync(Stream content, string path, UploadBudget budget,
		CancellationToken cancellationToken)
	{
		budget.AddFile();
		var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
			FileOptions.Asynchronous);
		await using (output.ConfigureAwait(false))
		{
			var buffer = new byte[81920];
			int read;
			while ((read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
			{
				budget.AddBytes(read);
				await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
			}
		}
	}

	/// <summary>Builds the full path of a relative path inside a folder, refusing any path that could lead elsewhere.</summary>
	/// <param name="folder">The folder's full path.</param>
	/// <param name="relative">The relative path, its folders separated by <c>/</c> or <c>\</c>.</param>
	/// <returns>The full path, or <see langword="null" /> when the path is empty, rooted, climbs out or has an unsafe name.</returns>
	private static string? PathInside(string folder, string relative)
	{
		var segments = relative.Split('/', '\\');
		if (relative.Length == 0 || Path.IsPathRooted(relative) || segments.Length > MaxPathSegments ||
		    segments.Any(segment => segment.Length == 0 || segment is "." or ".." ||
		                            segment.EndsWith('.') || segment.EndsWith(' ') ||
		                            segment.Any(char.IsControl) || segment.Any(InvalidNameChars.Contains) ||
		                            IsReservedName(segment)))
			return null;

		return Path.Combine([folder, .. segments]);
	}

	/// <summary>Builds the entry of an archive file lying in the storage.</summary>
	/// <param name="path">The archive's full path.</param>
	/// <param name="name">The archive's name.</param>
	/// <returns>The archive entry.</returns>
	private static FileBeatmapsetArchive ArchiveAt(string path, string name)
	{
		var info = new FileInfo(path);
		return new FileBeatmapsetArchive(path, name, info.Length,
			new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
	}

	/// <summary>Packs files of a folder into a new archive, each under its path inside the folder.</summary>
	/// <param name="folder">The folder's full path.</param>
	/// <param name="files">The full paths of the files to pack, all inside the folder.</param>
	/// <param name="target">The new archive's full path.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	private static async Task PackFilesAsync(string folder, IEnumerable<string> files, string target,
		CancellationToken cancellationToken)
	{
		var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
			FileOptions.Asynchronous);
		await using (stream.ConfigureAwait(false))
		{
			using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
			foreach (var file in files)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var entry = zip.CreateEntry(Path.GetRelativePath(folder, file).Replace('\\', '/'),
					CompressionLevel.Optimal);
				var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
					FileOptions.Asynchronous);
				await using (input.ConfigureAwait(false))
				{
					var output = entry.Open();
					await using (output.ConfigureAwait(false))
					{
						await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
					}
				}
			}
		}
	}

	/// <summary>Builds the full path of a new name in the storage that is never listed, watched or taken in.</summary>
	/// <param name="extension">The extension to end the name with, or empty for a folder.</param>
	/// <returns>The full path, on the same volume as the set folders it is moved into.</returns>
	private string Incoming(string extension = "")
	{
		return Path.Combine(paths.Beatmapsets.Path, $"{IncomingMarker}{Guid.NewGuid():N}{extension}");
	}

	/// <summary>Determines whether a top-level name is hidden from listing and watching.</summary>
	/// <param name="name">The name.</param>
	/// <returns><see langword="true" /> while the name is being removed or written.</returns>
	internal static bool IsHidden(string name)
	{
		return name.Contains(DeletedMarker, StringComparison.Ordinal) ||
		       name.StartsWith(IncomingMarker, StringComparison.Ordinal);
	}

	/// <summary>Removes an upload's folder, leaving what it cannot remove to the startup sweep.</summary>
	/// <param name="upload">The upload.</param>
	private void DeleteUpload(FileBeatmapsetUpload upload)
	{
		try
		{
			DeleteDirectoryIfExists(upload.Path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			logger.LogWarning(exception, "Could not remove the upload {Path}", upload.Path);
		}
	}

	/// <summary>Counts what one upload holds, uploaded and unpacked, against its limits.</summary>
	private sealed class UploadBudget
	{
		private long _bytes;
		private int _files;

		/// <summary>Counts one more file.</summary>
		/// <exception cref="InvalidDataException">The upload would hold too many files.</exception>
		public void AddFile()
		{
			if (++_files > MaxUploadFiles) throw new InvalidDataException("The upload holds too many files.");
		}

		/// <summary>Counts more bytes.</summary>
		/// <param name="count">The number of bytes.</param>
		/// <exception cref="InvalidDataException">The upload would hold too many bytes.</exception>
		public void AddBytes(long count)
		{
			if ((_bytes += count) > MaxUploadBytes) throw new InvalidDataException("The upload holds too many bytes.");
		}
	}

	/// <inheritdoc />
	public async Task SaveAsync(Beatmapset set, BeatmapsetArchiveEntry archive,
		CancellationToken cancellationToken = default)
	{
		if (archive is not FileBeatmapsetArchive placed)
			throw new ArgumentException("The archive does not belong to this storage.", nameof(archive));

		var target = Path.Combine(paths.Beatmapsets.Path, FolderName(set));
		var gate = SetLocks.GetOrAdd(set.Id, static _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var placedParent = Path.GetDirectoryName(placed.Path);
			var placedGrandparent = placedParent is null ? null : Path.GetDirectoryName(placedParent);
			var isSetFolderArchive = placedParent is not null &&
			                         placedGrandparent is not null &&
			                         string.Equals(placedGrandparent, paths.Beatmapsets.Path,
				                         StringComparison.OrdinalIgnoreCase) &&
			                         string.Equals(Path.GetFileName(placed.Path), ArchiveName,
				                         StringComparison.OrdinalIgnoreCase);

			if (isSetFolderArchive)
			{
				// The archive is already a set folder's archive.osz; move or rename the folder to the target.
				if (string.Equals(placedParent, target, StringComparison.OrdinalIgnoreCase))
				{
					// Same folder, at most a change of letter case.
					if (!string.Equals(placedParent, target, StringComparison.Ordinal))
					{
						var renaming = placedParent + ".renaming";
						Directory.Move(placedParent!, renaming);
						Directory.Move(renaming, target);
					}
				}
				else
				{
					if (Directory.Exists(target)) RemoveMarked(MarkDeleted(target), set);
					Directory.Move(placedParent!, target);
				}
			}
			else
			{
				// A loose .osz, an uploaded or a packed archive: build a set folder around it and move it into place.
				var stagingFolder = Incoming();
				Directory.CreateDirectory(stagingFolder);
				var stagedArchive = Path.Combine(stagingFolder, ArchiveName);
				string? marked = null;
				try
				{
					File.Move(placed.Path, stagedArchive);
					if (Directory.Exists(target)) marked = MarkDeleted(target);
					Directory.Move(stagingFolder, target);
				}
				catch
				{
					if (marked is not null && Directory.Exists(marked) && !Directory.Exists(target))
						Directory.Move(marked, target);
					// Never lose the archive: put it back where it was before dropping the staging folder.
					if (File.Exists(stagedArchive) && !File.Exists(placed.Path)) File.Move(stagedArchive, placed.Path);
					DeleteDirectoryIfExists(stagingFolder);
					throw;
				}

				if (marked is not null) RemoveMarked(marked, set);

				if (placed.PackedFrom is not null && Directory.Exists(placed.PackedFrom))
				{
					var packedMarked = MarkDeleted(placed.PackedFrom);
					RemoveMarked(packedMarked, set);
				}
			}

			foreach (var other in FoldersOf(set.Id).Where(folder =>
				         IsSetFolder(folder) && !string.Equals(folder, target, StringComparison.Ordinal)))
			{
				var otherMarked = MarkDeleted(other);
				RemoveMarked(otherMarked, set);
			}
		}
		finally
		{
			gate.Release();
		}
	}

	/// <inheritdoc />
	public async Task<BeatmapsetArchiveEntry?> PackAsync(BeatmapsetFolderEntry folder,
		CancellationToken cancellationToken = default)
	{
		if (folder is not FileBeatmapsetFolder placed)
			throw new ArgumentException("The folder does not belong to this storage.", nameof(folder));
		if (!Directory.Exists(placed.Path)) return null;
		// A set's folder that lost its archive still holds what was made from it: it is not loose files.
		if (Directory.Exists(Path.Combine(placed.Path, VersionFolder)) ||
		    File.Exists(Path.Combine(placed.Path, ArchiveWithoutVideoName)))
			return null;

		// Links are never followed: only what lies inside the folder itself is packed.
		if (new DirectoryInfo(placed.Path).Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
		var files = Directory.EnumerateFiles(placed.Path, "*",
				new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
			.ToList();
		// Difficulties directly in the folder make it one beatmapset; without them it is none.
		if (files.Count == 0 || !Directory.EnumerateFiles(placed.Path, "*.osu").Any()) return null;

		var path = Incoming(".osz");
		await PackFilesAsync(placed.Path, files, path, cancellationToken).ConfigureAwait(false);

		var info = new FileInfo(path);
		return new FileBeatmapsetArchive(path, folder.Name, info.Length,
			new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), packedFrom: placed.Path);
	}

	/// <inheritdoc />
	public async Task ClearAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		var gate = SetLocks.GetOrAdd(set.Id, static _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var folder = FindFolder(set.Id, FolderName(set));
			if (folder is null) return;

			DeleteFileIfExists(Path.Combine(folder, ArchiveWithoutVideoName));
			DeleteFileIfExists(Path.Combine(folder, StoryboardName));
			DeleteDirectoryIfExists(Path.Combine(folder, VersionFolder));
			DeleteDirectoryIfExists(Path.Combine(folder, BackgroundFolder));
			DeleteDirectoryIfExists(Path.Combine(folder, AudioFolder));
			DeleteDirectoryIfExists(Path.Combine(folder, VideoFolder));
		}
		finally
		{
			gate.Release();
		}
	}

	/// <inheritdoc />
	public async Task SaveAsync(Beatmapset set, BeatmapsetFileKind kind, string name, Stream content,
		CancellationToken cancellationToken = default)
	{
		var gate = SetLocks.GetOrAdd(set.Id, static _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var folder = FindFolder(set.Id, FolderName(set));
			if (folder is null) throw new InvalidOperationException("The beatmapset has no folder.");

			var path = PathOf(folder, kind, name);
			var parent = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

			var temporary = Incoming(".tmp");
			try
			{
				await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
					             FileShare.None,
					             81920, FileOptions.Asynchronous))
				{
					await content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
				}

				File.Move(temporary, path, overwrite: true);
			}
			finally
			{
				if (File.Exists(temporary)) File.Delete(temporary);
			}
		}
		finally
		{
			gate.Release();
		}
	}

	/// <inheritdoc />
	public async Task SaveArchiveWithoutVideoAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		var gate = SetLocks.GetOrAdd(set.Id, static _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var folder = FindFolder(set.Id, FolderName(set));
			if (folder is null) throw new InvalidOperationException("The beatmapset has no folder.");

			var archivePath = Path.Combine(folder, ArchiveName);
			if (!File.Exists(archivePath)) throw new InvalidOperationException("The beatmapset has no archive.");

			var targetPath = Path.Combine(folder, ArchiveWithoutVideoName);
			var temporary = Incoming(".osz");
			try
			{
				await using (var input = new FileStream(archivePath, FileMode.Open, FileAccess.Read,
					             FileShare.Read | FileShare.Delete, 81920, FileOptions.Asynchronous))
				{
					using var source = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
					await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
						FileShare.None, 81920, FileOptions.Asynchronous);
					using var destination = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
					foreach (var entry in source.Entries.Where(entry => !entry.FullName.EndsWith('/')))
					{
						cancellationToken.ThrowIfCancellationRequested();
						if (VideoExtensions.Contains(Path.GetExtension(entry.FullName))) continue;

						var created = destination.CreateEntry(entry.FullName, CompressionLevel.Optimal);
						await using var entryInput = entry.Open();
						await using var entryOutput = created.Open();
						await entryInput.CopyToAsync(entryOutput, cancellationToken).ConfigureAwait(false);
					}
				}

				File.Move(temporary, targetPath, overwrite: true);
			}
			finally
			{
				if (File.Exists(temporary)) File.Delete(temporary);
			}
		}
		finally
		{
			gate.Release();
		}
	}

	/// <inheritdoc />
	public Task<bool> ExistsAsync(Beatmapset set, BeatmapsetFileKind kind, string name,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var folder = FindFolder(set.Id, FolderName(set));
		if (folder is null) return Task.FromResult(false);

		try
		{
			return Task.FromResult(File.Exists(PathOf(folder, kind, name)));
		}
		catch (ArgumentException)
		{
			return Task.FromResult(false);
		}
	}

	/// <inheritdoc />
	public Task<Stream?> OpenAsync(Beatmapset set, BeatmapsetFileKind kind, string name,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var folder = FindFolder(set.Id, FolderName(set));
		if (folder is null) return Task.FromResult<Stream?>(null);

		string path;
		try
		{
			path = PathOf(folder, kind, name);
		}
		catch (ArgumentException)
		{
			return Task.FromResult<Stream?>(null);
		}

		if (!File.Exists(path)) return Task.FromResult<Stream?>(null);
		return Task.FromResult<Stream?>(OpenRead(path));
	}

	/// <inheritdoc />
	public Task<Stream?> OpenArchiveAsync(Beatmapset set, bool withVideo,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var folder = FindFolder(set.Id, FolderName(set));
		if (folder is null) return Task.FromResult<Stream?>(null);

		var name = withVideo ? ArchiveName : ArchiveWithoutVideoName;
		var path = Path.Combine(folder, name);
		if (!File.Exists(path)) return Task.FromResult<Stream?>(null);
		return Task.FromResult<Stream?>(OpenRead(path));
	}

	/// <inheritdoc />
	public Task<IEnumerable<BeatmapsetEntry>> ListAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var entries = new List<BeatmapsetEntry>();
		if (Directory.Exists(paths.Beatmapsets.Path))
		{
			foreach (var folder in Directory.EnumerateDirectories(paths.Beatmapsets.Path))
			{
				var name = Path.GetFileName(folder);
				if (IsHidden(name)) continue;
				var entry = EntryOf(name);
				if (entry is not null) entries.Add(entry);
			}

			foreach (var file in Directory.EnumerateFiles(paths.Beatmapsets.Path))
			{
				var name = Path.GetFileName(file);
				if (IsHidden(name) ||
				    !IsArchiveName(name)) continue;
				var entry = EntryOf(name);
				if (entry is not null) entries.Add(entry);
			}
		}

		return Task.FromResult<IEnumerable<BeatmapsetEntry>>(entries);
	}

	/// <inheritdoc />
	public IAsyncEnumerable<BeatmapsetEntry> WatchAsync(CancellationToken cancellationToken)
	{
		var settled = Channel.CreateUnbounded<string>();
		var pending = new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
		var gate = new Lock();
		var watcher = new FileSystemWatcher(paths.Beatmapsets.Path)
		{
			IncludeSubdirectories = true,
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite |
			               NotifyFilters.Size
		};

		watcher.Created += (_, args) => ScheduleName(TopLevelName(args.FullPath));
		watcher.Changed += (_, args) => ScheduleName(TopLevelName(args.FullPath));
		watcher.Deleted += (_, args) => ScheduleName(TopLevelName(args.FullPath));
		watcher.Renamed += (_, args) =>
		{
			ScheduleName(TopLevelName(args.OldFullPath));
			ScheduleName(TopLevelName(args.FullPath));
		};
		watcher.Error += (sender, args) => _ = ReportEveryEntryAsync(args.GetException());
		watcher.EnableRaisingEvents = true;

		return WatchEntriesAsync(watcher, settled, pending, gate, cancellationToken);

		void ScheduleName(string? name)
		{
			if (name is null || IsHidden(name) ||
			    cancellationToken.IsCancellationRequested)
				return;

			lock (gate)
			{
				if (pending.Remove(name, out var previous)) previous.Cancel();
				pending.Add(name, new CancellationTokenSource());
			}

			_ = SettleAsync(name);
		}

		async Task SettleAsync(string name)
		{
			CancellationTokenSource debounce;
			lock (gate)
			{
				if (!pending.TryGetValue(name, out debounce!)) return;
			}

			try
			{
				await Task.Delay(TimeSpan.FromSeconds(2), time, debounce.Token).ConfigureAwait(false);
				lock (gate)
				{
					if (!pending.TryGetValue(name, out var current) || !ReferenceEquals(current, debounce)) return;
					pending.Remove(name);
				}

				await settled.Writer.WriteAsync(name, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (debounce.IsCancellationRequested ||
			                                         cancellationToken.IsCancellationRequested)
			{
			}
			catch (Exception exception)
			{
				logger.LogWarning(exception, "Could not report the changed beatmapset entry {Name}", name);
			}
			finally
			{
				debounce.Dispose();
			}
		}

		async Task ReportEveryEntryAsync(Exception? exception)
		{
			logger.LogWarning(exception, "Watching {Directory} failed; reporting every entry", paths.Beatmapsets.Path);
			try
			{
				foreach (var entry in await ListAsync(cancellationToken).ConfigureAwait(false))
					ScheduleName(entry.Name);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
			}
			catch (Exception list)
			{
				logger.LogWarning(list, "Could not list the beatmapset entries to report them again");
			}
		}

		string? TopLevelName(string fullPath)
		{
			var relative = Path.GetRelativePath(paths.Beatmapsets.Path, fullPath);
			if (string.IsNullOrEmpty(relative) || relative == ".") return null;
			var separator = relative.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
			return separator < 0 ? relative : relative[..separator];
		}
	}

	/// <summary>Yields each watched entry once its changes have settled, disposing the watcher when the watching ends.</summary>
	private async IAsyncEnumerable<BeatmapsetEntry> WatchEntriesAsync(FileSystemWatcher watcher,
		Channel<string> settled, Dictionary<string, CancellationTokenSource> pending, Lock gate,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		try
		{
			await foreach (var name in settled.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
			{
				var entry = EntryOf(name);
				if (entry is not null) yield return entry;
			}
		}
		finally
		{
			watcher.Dispose();
			lock (gate)
			{
				foreach (var debounce in pending.Values) debounce.Cancel();
				pending.Clear();
			}
		}
	}

	/// <inheritdoc />
	public async Task DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		var gate = SetLocks.GetOrAdd(set.Id, static _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			foreach (var folder in FoldersOf(set.Id).Where(IsSetFolder))
				RemoveMarked(MarkDeleted(folder), set);
		}
		finally
		{
			gate.Release();
		}
	}

	/// <summary>Builds an entry for a top-level name, or <see langword="null" /> when it is not an entry.</summary>
	/// <param name="name">The entry's name in the storage.</param>
	/// <returns>The entry, or <see langword="null" /> when the name is neither a folder nor an .osz archive.</returns>
	private BeatmapsetEntry? EntryOf(string name)
	{
		var path = Path.Combine(paths.Beatmapsets.Path, name);
		// A linked entry points outside the storage; it is never taken in, changed or deleted.
		if ((File.Exists(path) || Directory.Exists(path)) &&
		    File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
			return null;

		if (Directory.Exists(path))
		{
			var archivePath = Path.Combine(path, ArchiveName);
			BeatmapsetArchiveEntry? archive = null;
			if (File.Exists(archivePath))
			{
				var info = new FileInfo(archivePath);
				archive = new FileBeatmapsetArchive(archivePath, ArchiveName, info.Length,
					new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
			}

			if (archive is null && IsCollection(path)) return new FileBeatmapsetCollection(path, name);
			return new FileBeatmapsetFolder(path, name, LeadingId(name), true, archive);
		}

		// Only an archive is an entry; any other file placed here, such as a loose difficulty, is not.
		if (File.Exists(path))
		{
			if (!IsArchiveName(name)) return null;
			return HoldsBeatmapsets(path) ? new FileBeatmapsetCollection(path, name) : ArchiveAt(path, name);
		}

		if (!IsArchiveName(name))
			return new FileBeatmapsetFolder(path, name, LeadingId(name), false, null);

		return null;
	}

	/// <summary>Builds the path of one file made from a set's archive.</summary>
	/// <param name="setFolder">The set folder's full path.</param>
	/// <param name="kind">What the file is.</param>
	/// <param name="name">The plain file name to store it under.</param>
	/// <returns>The file's full path.</returns>
	/// <exception cref="ArgumentException"><paramref name="name" /> is not a plain file name.</exception>
	private static string PathOf(string setFolder, BeatmapsetFileKind kind, string name)
	{
		if (string.IsNullOrEmpty(name))
			throw new ArgumentException("The file name cannot be empty.", nameof(name));
		if (name != Path.GetFileName(name))
			throw new ArgumentException("The file name must not contain a path.", nameof(name));
		if (name.Any(char.IsControl) || name.Any(InvalidNameChars.Contains))
			throw new ArgumentException("The file name contains invalid characters.", nameof(name));
		if (IsReservedName(name) || name.EndsWith('.') || name.EndsWith(' '))
			throw new ArgumentException("The file name is not one a file may have.", nameof(name));

		return kind switch
		{
			BeatmapsetFileKind.Storyboard => Path.Combine(setFolder, StoryboardName),
			BeatmapsetFileKind.Preview => Path.Combine(setFolder, AudioFolder, PreviewPrefix + name),
			BeatmapsetFileKind.Version => Path.Combine(setFolder, VersionFolder, name),
			BeatmapsetFileKind.Background => Path.Combine(setFolder, BackgroundFolder, name),
			BeatmapsetFileKind.Audio => Path.Combine(setFolder, AudioFolder, name),
			BeatmapsetFileKind.Video => Path.Combine(setFolder, VideoFolder, name),
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
		};
	}

	/// <summary>Reads the beatmapset id a folder or file name starts with.</summary>
	/// <param name="name">The name to read.</param>
	/// <returns>
	///     The id formed by the leading digits when they are followed by the end of the name or a space; otherwise,
	///     <see langword="null" />.
	/// </returns>
	internal static int? LeadingId(string name)
	{
		var digits = 0;
		while (digits < name.Length && char.IsAsciiDigit(name[digits])) digits++;
		if (digits == 0 || (digits < name.Length && name[digits] != ' ')) return null;
		return int.TryParse(name.AsSpan(0, digits), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
			? id
			: null;
	}

	/// <summary>Builds the canonical name of a set's folder: its id, artist and title as osu! names its songs, without the characters a folder name may not have.</summary>
	/// <param name="set">The beatmapset the folder holds.</param>
	/// <returns>The folder's name.</returns>
	private static string FolderName(Beatmapset set)
	{
		var name = $"{set.Id} {set.Value.Artist} - {set.Value.Title}";
		var builder = new StringBuilder(name.Length);
		foreach (var character in name)
			if (!char.IsControl(character) && !InvalidNameChars.Contains(character))
				builder.Append(character);

		var folder = builder.ToString().TrimEnd('.', ' ');
		if (folder.Length > 150) folder = folder[..150].TrimEnd('.', ' ');
		return folder;
	}

	/// <summary>Determines whether a folder is a set's folder: it holds an archive or files made from one, never loose files.</summary>
	/// <param name="folder">The folder's full path.</param>
	/// <returns><see langword="true" /> if the folder holds an archive or what was made from one.</returns>
	private static bool IsSetFolder(string folder)
	{
		return File.Exists(Path.Combine(folder, ArchiveName)) ||
		       File.Exists(Path.Combine(folder, ArchiveWithoutVideoName)) ||
		       Directory.Exists(Path.Combine(folder, VersionFolder));
	}

	/// <summary>Lists every folder stored for a beatmapset, whatever its name after the id.</summary>
	/// <param name="id">The beatmapset id the folders' names start with.</param>
	/// <returns>The folders' full paths, ordered by name; empty when none is stored.</returns>
	private List<string> FoldersOf(int id)
	{
		if (!Directory.Exists(paths.Beatmapsets.Path)) return [];
		return (from folder in Directory.EnumerateDirectories(paths.Beatmapsets.Path)
			let name = Path.GetFileName(folder)
			where !IsHidden(name) && LeadingId(name) == id
			select folder).Order(StringComparer.Ordinal).ToList();
	}

	/// <summary>Finds the folder stored for a beatmapset, preferring the one named after the set.</summary>
	/// <param name="id">The beatmapset id the folder's name starts with.</param>
	/// <param name="preferredName">The folder name to prefer, or <see langword="null" /> for the first by name.</param>
	/// <returns>The folder's full path, or <see langword="null" /> when no folder is stored for the set.</returns>
	private string? FindFolder(int id, string? preferredName)
	{
		var candidates = FoldersOf(id);
		if (candidates.Count == 0) return null;
		return (preferredName is null
			       ? null
			       : candidates.FirstOrDefault(folder =>
				       string.Equals(Path.GetFileName(folder), preferredName, StringComparison.OrdinalIgnoreCase)))
		       ?? candidates.MinBy(folder => Path.GetFileName(folder), StringComparer.Ordinal);
	}

	/// <summary>Renames a folder out of the way before its files are removed, so nothing new is stored under its name meanwhile.</summary>
	/// <param name="folder">The folder's full path.</param>
	/// <returns>The folder's new path, carrying the deleted marker.</returns>
	/// <exception cref="IOException">The folder could not be renamed; nothing was changed.</exception>
	private static string MarkDeleted(string folder)
	{
		var marked = folder + DeletedMarker + Guid.NewGuid().ToString("N");
		Directory.Move(folder, marked);
		return marked;
	}

	/// <summary>Removes a folder that was renamed out of the way, leaving what is still in use to the startup sweep.</summary>
	/// <param name="markedFolder">The folder's renamed path.</param>
	/// <param name="set">The beatmapset whose previous files these are.</param>
	private void RemoveMarked(string markedFolder, Beatmapset set)
	{
		try
		{
			Directory.Delete(markedFolder, true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			logger.LogWarning(exception, "Could not remove the previous files of beatmapset {SetId}", set.Id);
		}
	}

	private static FileStream OpenRead(string path)
	{
		return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920,
			FileOptions.Asynchronous);
	}

	private static void DeleteFileIfExists(string path)
	{
		if (File.Exists(path)) File.Delete(path);
	}

	private static void DeleteDirectoryIfExists(string path)
	{
		if (Directory.Exists(path)) Directory.Delete(path, true);
	}
}