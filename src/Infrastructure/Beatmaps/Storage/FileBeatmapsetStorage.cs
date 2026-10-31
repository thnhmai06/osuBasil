using System.IO.Compression;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Domain.Beatmaps;
using Basil.Domain.Content;
using Basil.Infrastructure.Common.Storage;
using Microsoft.Extensions.Logging;
using static Basil.Infrastructure.Beatmaps.Storage.FileBeatmapsetEntryReader;
using static Basil.Infrastructure.Beatmaps.Storage.FileBeatmapsetNames;

namespace Basil.Infrastructure.Beatmaps.Storage;

/// <summary>
///     Stores each beatmapset's archive and the files made from it in its own folder of Data/Beatmapsets, and takes
///     in what is placed there.
/// </summary>
internal sealed class FileBeatmapsetStorage(
	DataPaths paths,
	TimeProvider time,
	ILogger<FileBeatmapsetStorage> logger) : IBeatmapsetStorage
{
	private const int BufferSize = 81920;

	private readonly Dictionary<int, SetLock> _setLocks = [];

	/// <inheritdoc />
	public async Task<BeatmapsetArchive> SaveAsync(Stream archive, CancellationToken cancellationToken = default)
	{
		var path = Incoming(".osz");
		try
		{
			var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize,
				FileOptions.Asynchronous);
			await using (output)
			{
				await archive.CopyToAsync(output, cancellationToken);
			}
		}
		catch
		{
			DeleteFileIfExists(path);
			throw;
		}

		return ArchiveAt(path, Path.GetFileName(path));
	}

	/// <inheritdoc />
	public Task DeleteAsync(BeatmapsetArchive archive, CancellationToken cancellationToken = default)
	{
		if (archive is not FileBeatmapsetArchive placed ||
		    (!IsHidden(Path.GetFileName(placed.Path)) && placed.PackedFrom is null))
			throw new ArgumentException("The archive was neither uploaded nor packed by this storage.",
				nameof(archive));

		try
		{
			DeleteFileIfExists(placed.Path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			logger.LogWarning(exception, "Could not remove the archive {Path}", placed.Path);
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task SaveAsync(BeatmapsetRecord set, BeatmapsetArchive archive,
		CancellationToken cancellationToken = default)
	{
		if (archive is not FileBeatmapsetArchive placed)
			throw new ArgumentException("The archive does not belong to this storage.", nameof(archive));

		var target = Path.Combine(paths.Beatmapsets.Path, FolderName(set));
		var gate = await LockAsync(set.Id, cancellationToken);
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
				MoveSetFolder(placedParent!, target);
			else
				PlaceArchive(placed, set, target);

			foreach (var other in FoldersOf(set.Id).Where(folder =>
				         IsSetFolder(folder) && !string.Equals(folder, target, StringComparison.Ordinal)))
				Directory.Delete(other, true);
		}
		finally
		{
			gate.Release();
		}
	}

	/// <inheritdoc />
	public async Task<BeatmapsetArchive?> PackAsync(BeatmapsetFolder folder,
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
		try
		{
			await PackFilesAsync(placed.Path, files, path, cancellationToken);
		}
		catch
		{
			DeleteFileIfExists(path);
			throw;
		}

		var info = new FileInfo(path);
		return new FileBeatmapsetArchive(path, folder.Name,
			new ContentStamp(info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)), placed.Path);
	}

	/// <inheritdoc />
	public async Task ClearAsync(BeatmapsetRecord set, CancellationToken cancellationToken = default)
	{
		var gate = await LockAsync(set.Id, cancellationToken);
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
	public async Task SaveAsync(BeatmapsetRecord set, BeatmapsetFileType type, string name, Stream content,
		CancellationToken cancellationToken = default)
	{
		var gate = await LockAsync(set.Id, cancellationToken);
		try
		{
			var folder = FindFolder(set.Id, FolderName(set)) ??
			             throw new InvalidOperationException("The beatmapset has no folder.");
			var path = PathOf(folder, type, name);
			var parent = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

			var temporary = Incoming(".tmp");
			try
			{
				await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
					             FileShare.None,
					             BufferSize, FileOptions.Asynchronous))
				{
					await content.CopyToAsync(output, cancellationToken);
				}

				File.Move(temporary, path, true);
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
	public async Task SaveArchiveWithoutVideoAsync(BeatmapsetRecord set, IReadOnlyCollection<string> videoFiles,
		CancellationToken cancellationToken = default)
	{
		var videos = videoFiles.Select(name => name.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var gate = await LockAsync(set.Id, cancellationToken);
		try
		{
			var folder = FindFolder(set.Id, FolderName(set)) ??
			             throw new InvalidOperationException("The beatmapset has no folder.");
			var archivePath = Path.Combine(folder, ArchiveName);
			if (!File.Exists(archivePath)) throw new InvalidOperationException("The beatmapset has no archive.");

			var targetPath = Path.Combine(folder, ArchiveWithoutVideoName);
			var temporary = Incoming(".osz");
			try
			{
				await using (var input = new FileStream(archivePath, FileMode.Open, FileAccess.Read,
					             FileShare.Read | FileShare.Delete, BufferSize, FileOptions.Asynchronous))
				{
					await using var source = new ZipArchive(input, ZipArchiveMode.Read, true);
					await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
						FileShare.None, BufferSize, FileOptions.Asynchronous);
					await using var destination = new ZipArchive(output, ZipArchiveMode.Create, true);
					foreach (var entry in source.Entries.Where(entry => !entry.FullName.EndsWith('/')))
					{
						cancellationToken.ThrowIfCancellationRequested();
						if (videos.Contains(entry.FullName.Replace('\\', '/'))) continue;

						var created = destination.CreateEntry(entry.FullName, CompressionLevel.Optimal);
						await using var entryInput = await entry.OpenAsync(cancellationToken);
						await using var entryOutput = await created.OpenAsync(cancellationToken);
						await entryInput.CopyToAsync(entryOutput, cancellationToken);
					}
				}

				File.Move(temporary, targetPath, true);
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
	public Task<bool> ExistsAsync(BeatmapsetRecord set, BeatmapsetFileType type, string name,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var folder = FindFolder(set.Id, FolderName(set));
		if (folder is null) return Task.FromResult(false);

		try
		{
			return Task.FromResult(File.Exists(PathOf(folder, type, name)));
		}
		catch (ArgumentException)
		{
			return Task.FromResult(false);
		}
	}

	/// <inheritdoc />
	public Task<Stream?> OpenAsync(BeatmapsetRecord set, BeatmapsetFileType type, string name,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var folder = FindFolder(set.Id, FolderName(set));
		if (folder is null) return Task.FromResult<Stream?>(null);

		string path;
		try
		{
			path = PathOf(folder, type, name);
		}
		catch (ArgumentException)
		{
			return Task.FromResult<Stream?>(null);
		}

		return Task.FromResult<Stream?>(File.Exists(path) ? OpenRead(path) : null);
	}

	/// <inheritdoc />
	public Task<Stream?> OpenArchiveAsync(BeatmapsetRecord set, bool withVideo,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var folder = FindFolder(set.Id, FolderName(set));
		if (folder is null) return Task.FromResult<Stream?>(null);

		var name = withVideo ? ArchiveName : ArchiveWithoutVideoName;
		var path = Path.Combine(folder, name);
		return Task.FromResult<Stream?>(File.Exists(path) ? OpenRead(path) : null);
	}

	/// <inheritdoc />
	public Task<IEnumerable<BeatmapsetSource>> ListAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult(ListEntries());
	}

	/// <inheritdoc />
	public IAsyncEnumerable<BeatmapsetSource> WatchAsync(CancellationToken cancellationToken)
	{
		return new FileBeatmapsetWatcher(paths.Beatmapsets.Path, time, logger, ListEntries, cancellationToken);
	}

	/// <inheritdoc />
	public async Task DeleteAsync(BeatmapsetRecord set, CancellationToken cancellationToken = default)
	{
		var gate = await LockAsync(set.Id, cancellationToken);
		try
		{
			foreach (var folder in FoldersOf(set.Id).Where(IsSetFolder))
				Directory.Delete(folder, true);
		}
		finally
		{
			gate.Release();
		}
	}

	/// <summary>Moves the folder that already holds the archive to the folder the set is stored in.</summary>
	/// <param name="placedParent">The folder that holds the archive.</param>
	/// <param name="target">The folder the set is stored in.</param>
	private void MoveSetFolder(string placedParent, string target)
	{
		if (string.Equals(placedParent, target, StringComparison.OrdinalIgnoreCase))
		{
			// Same folder, at most a change of letter case.
			if (!string.Equals(placedParent, target, StringComparison.Ordinal))
			{
				var renaming = Incoming();
				Directory.Move(placedParent, renaming);
				Directory.Move(renaming, target);
			}
		}
		else
		{
			DeleteDirectoryIfExists(target);
			Directory.Move(placedParent, target);
		}
	}

	/// <summary>Moves a loose, uploaded or packed archive into a folder of its own for the set.</summary>
	/// <param name="placed">The archive to place, as the caller holds it.</param>
	/// <param name="set">The beatmapset the archive holds.</param>
	/// <param name="target">The folder the set is stored in.</param>
	private void PlaceArchive(FileBeatmapsetArchive placed, BeatmapsetRecord set, string target)
	{
		// A loose .osz, an uploaded or a packed archive: build a set folder around it and move it into place.
		var stagingFolder = Incoming();
		Directory.CreateDirectory(stagingFolder);
		var stagedArchive = Path.Combine(stagingFolder, ArchiveName);
		string? aside = null;
		try
		{
			File.Move(placed.Path, stagedArchive);
			if (Directory.Exists(target))
			{
				aside = Incoming();
				Directory.Move(target, aside);
			}

			Directory.Move(stagingFolder, target);
		}
		catch
		{
			RestoreAfterFailure(placed, target, stagingFolder, stagedArchive, aside);
			throw;
		}

		if (aside is not null) RemoveAside(aside, set);

		DeletePackedSource(placed, target);
	}

	private void RestoreAfterFailure(FileBeatmapsetArchive placed, string target, string stagingFolder,
		string stagedArchive,
		string? aside)
	{
		if (aside is not null && Directory.Exists(aside) && !Directory.Exists(target))
			Directory.Move(aside, target);
		// Never lose the archive: put it back where it was before dropping the staging folder.
		if (File.Exists(stagedArchive) && !File.Exists(placed.Path)) File.Move(stagedArchive, placed.Path);
		DeleteDirectoryIfExists(stagingFolder);
	}

	private static void DeletePackedSource(FileBeatmapsetArchive placed, string target)
	{
		if (placed.PackedFrom is not null && Directory.Exists(placed.PackedFrom) &&
		    !SamePath(placed.PackedFrom, target))
			Directory.Delete(placed.PackedFrom, true);
	}

	/// <summary>Waits for the turn on one beatmapset; operations on different sets do not wait for each other.</summary>
	/// <param name="id">The id of the beatmapset.</param>
	/// <param name="cancellationToken">A token that cancels the wait.</param>
	/// <returns>The turn; releasing it lets the next operation on the set run.</returns>
	private async Task<SetLock> LockAsync(int id, CancellationToken cancellationToken)
	{
		SetLock gate;
		lock (_setLocks)
		{
			if (!_setLocks.TryGetValue(id, out gate!))
				_setLocks[id] = gate = new SetLock(this, id);
			gate.Users++;
		}

		try
		{
			await gate.Semaphore.WaitAsync(cancellationToken);
		}
		catch
		{
			gate.Forget();
			throw;
		}

		return gate;
	}

	private IEnumerable<BeatmapsetSource> ListEntries()
	{
		var root = paths.Beatmapsets.Path;
		var entries = new List<BeatmapsetSource>();
		if (Directory.Exists(root))
		{
			entries.AddRange(Directory.EnumerateDirectories(root)
				.Where(folder => !IsHidden(Path.GetFileName(folder)))
				.Select(folder => EntryOf(root, Path.GetFileName(folder)))
				.OfType<BeatmapsetSource>());

			entries.AddRange(Directory.EnumerateFiles(root)
				.Select(file => Path.GetFileName(file))
				.Where(name => !IsHidden(name) && IsArchiveName(name))
				.Select(name => EntryOf(root, name))
				.OfType<BeatmapsetSource>());
		}

		return entries;
	}

	/// <summary>Packs files of a folder into a new archive, each under its path inside the folder.</summary>
	/// <param name="folder">The folder's full path.</param>
	/// <param name="files">The full paths of the files to pack, all inside the folder.</param>
	/// <param name="target">The new archive's full path.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	private static async Task PackFilesAsync(string folder, IEnumerable<string> files, string target,
		CancellationToken cancellationToken)
	{
		var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize,
			FileOptions.Asynchronous);
		await using (stream)
		{
			await using var zip = new ZipArchive(stream, ZipArchiveMode.Create, true);
			foreach (var file in files)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var entry = zip.CreateEntry(Path.GetRelativePath(folder, file).Replace('\\', '/'),
					CompressionLevel.Optimal);
				var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
					FileOptions.Asynchronous);
				await using (input)
				{
					var output = await entry.OpenAsync(cancellationToken);
					await using (output)
					{
						await input.CopyToAsync(output, cancellationToken);
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

	/// <summary>Determines whether two paths name the same file or folder on this file system.</summary>
	private static bool SamePath(string left, string right)
	{
		var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		return string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
			Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), comparison);
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
		return
		[
			.. (from folder in Directory.EnumerateDirectories(paths.Beatmapsets.Path)
				let name = Path.GetFileName(folder)
				where !IsHidden(name) && LeadingId(name) == id
				select folder).Order(StringComparer.Ordinal)
		];
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

	/// <summary>Removes the previous folder of a set that was moved aside while the new one was moved in.</summary>
	/// <param name="asideFolder">The folder's path outside the set's name.</param>
	/// <param name="set">The beatmapset whose previous files these are.</param>
	private void RemoveAside(string asideFolder, BeatmapsetRecord set)
	{
		try
		{
			Directory.Delete(asideFolder, true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			logger.LogWarning(exception, "Could not remove the previous files of beatmapset {SetId}", set.Id);
		}
	}

	private static FileStream OpenRead(string path)
	{
		return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, BufferSize,
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

	/// <summary>The turn on one beatmapset, forgotten once nobody waits for it.</summary>
	private sealed class SetLock(FileBeatmapsetStorage storage, int id)
	{
		public SemaphoreSlim Semaphore { get; } = new(1, 1);
		public int Users { get; set; }

		public void Release()
		{
			Semaphore.Release();
			Forget();
		}

		public void Forget()
		{
			lock (storage._setLocks)
			{
				if (--Users == 0)
					storage._setLocks.Remove(id);
			}
		}
	}
}