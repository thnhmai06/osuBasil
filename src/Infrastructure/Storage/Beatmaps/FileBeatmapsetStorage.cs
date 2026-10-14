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

	private static readonly ConcurrentDictionary<int, SemaphoreSlim> SetLocks = new();

	/// <inheritdoc />
	public async Task<BeatmapsetArchiveEntry> SaveAsync(Stream archive,
		CancellationToken cancellationToken = default)
	{
		var path = Path.Combine(paths.Staging.Path, $"{Guid.NewGuid():N}.osz");
		var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
			FileOptions.Asynchronous);
		await using (output.ConfigureAwait(false))
		{
			await archive.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
		}

		var info = new FileInfo(path);
		return new FileBeatmapsetArchive(path, info.Name, info.Length,
			new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
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
				// A loose .osz, a staging upload, or a packed archive: stage it as a set folder and move into place.
				var stagingFolder = Path.Combine(paths.Staging.Path, Guid.NewGuid().ToString("N"));
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

			foreach (var other in FoldersOf(set.Id).Where(folder => !string.Equals(folder, target, StringComparison.Ordinal)))
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

		var files = Directory.EnumerateFiles(placed.Path, "*", SearchOption.AllDirectories).ToList();
		if (files.Count == 0) return null;

		var path = Path.Combine(paths.Staging.Path, $"{Guid.NewGuid():N}.osz");
		await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
				     FileOptions.Asynchronous))
		{
			using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
			foreach (var file in files)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var entryName = Path.GetRelativePath(placed.Path, file).Replace('\\', '/');
				var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
				await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
					FileOptions.Asynchronous);
				await using var output = entry.Open();
				await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
			}
		}

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

			var temporary = Path.Combine(paths.Staging.Path, $"{Guid.NewGuid():N}.tmp");
			try
			{
				await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
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
			var temporary = Path.Combine(paths.Staging.Path, $"{Guid.NewGuid():N}.osz");
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
				if (name.Contains(DeletedMarker, StringComparison.Ordinal)) continue;
				var entry = EntryOf(name);
				if (entry is not null) entries.Add(entry);
			}

			foreach (var file in Directory.EnumerateFiles(paths.Beatmapsets.Path))
			{
				var name = Path.GetFileName(file);
				if (name.Contains(DeletedMarker, StringComparison.Ordinal) ||
				    !name.EndsWith(".osz", StringComparison.OrdinalIgnoreCase)) continue;
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
			if (name is null || name.Contains(DeletedMarker, StringComparison.Ordinal) ||
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
			foreach (var folder in FoldersOf(set.Id))
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

			return new FileBeatmapsetFolder(path, name, LeadingId(name), true, archive);
		}

		if (File.Exists(path) && name.EndsWith(".osz", StringComparison.OrdinalIgnoreCase))
		{
			var info = new FileInfo(path);
			return new FileBeatmapsetArchive(path, name, info.Length,
				new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
		}

		if (!name.EndsWith(".osz", StringComparison.OrdinalIgnoreCase))
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

	/// <summary>Lists every folder stored for a beatmapset, whatever its name after the id.</summary>
	/// <param name="id">The beatmapset id the folders' names start with.</param>
	/// <returns>The folders' full paths, ordered by name; empty when none is stored.</returns>
	private List<string> FoldersOf(int id)
	{
		if (!Directory.Exists(paths.Beatmapsets.Path)) return [];
		return (from folder in Directory.EnumerateDirectories(paths.Beatmapsets.Path)
			let name = Path.GetFileName(folder)
			where !name.Contains(DeletedMarker, StringComparison.Ordinal) && LeadingId(name) == id
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
