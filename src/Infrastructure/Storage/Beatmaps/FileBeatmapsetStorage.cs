using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Storage.Common.Options;
using Basil.Infrastructure.Storage.Common.Queries;
using Microsoft.Extensions.Logging;
using SharpZip = ICSharpCode.SharpZipLib.Zip;

namespace Basil.Infrastructure.Storage.Beatmaps;

/// <summary>Stores beatmapset files and offers each set as an .osz archive.</summary>
internal sealed class FileBeatmapsetStorage(DataPaths paths, ILogger<FileBeatmapsetStorage> logger) : IBeatmapsetStorage
{
	// ponytail: 2,000 files and 2 GiB declared unpacked; raise these only if legitimate osu! sets exceed them.
	private const int MaxFiles = 2_000;
	private const long MaxUnpackedBytes = 2L * 1024 * 1024 * 1024;

	private static readonly FrozenSet<string> VideoExtensions =
		new[] { ".mp4", ".avi", ".flv", ".m4v", ".mkv", ".mov", ".mpg", ".mpeg", ".webm", ".wmv" }.ToFrozenSet(
			StringComparer.OrdinalIgnoreCase);

	private static readonly ConcurrentDictionary<int, SemaphoreSlim> SetLocks = new();

	/// <inheritdoc />
	public async Task SaveAsync(Beatmapset set, Stream archive, CancellationToken cancellationToken = default)
	{
		var id = set.Id.ToString(CultureInfo.InvariantCulture);
		var archiveFolder = ArchiveFolder(id);
		var setFolder = SetFolder(id);
		var uploadPath = Path.Combine(archiveFolder, $"upload-{Guid.NewGuid():N}.tmp");
		var unpackFolder = Path.Combine(paths.Beatmaps, $"{id}.unpack-{Guid.NewGuid():N}");
		var gate = SetLocks.GetOrAdd(set.Id, static _ => new SemaphoreSlim(1, 1));

		await gate.WaitAsync(cancellationToken);
		try
		{
			Directory.CreateDirectory(archiveFolder);
			await using (var upload = new FileStream(uploadPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
				             81920, FileOptions.Asynchronous))
			{
				await archive.CopyToAsync(upload, cancellationToken);
			}

			try
			{
				Unpack(uploadPath, unpackFolder, cancellationToken);
			}
			catch (Exception exception) when (exception is SharpZip.ZipException or InvalidDataException
				                                  or ArgumentException)
			{
				DeleteDirectoryIfExists(unpackFolder);
				throw new InvalidDataException($"The beatmapset archive is invalid: {exception.Message}", exception);
			}

			var oldFolder = Path.Combine(paths.Beatmaps, $"{id}.old-{Guid.NewGuid():N}");
			var movedOldFolder = false;
			if (Directory.Exists(setFolder))
			{
				Directory.Move(setFolder, oldFolder);
				movedOldFolder = true;
			}

			try
			{
				Directory.Move(unpackFolder, setFolder);
			}
			catch
			{
				if (movedOldFolder && !Directory.Exists(setFolder)) Directory.Move(oldFolder, setFolder);
				throw;
			}

			if (movedOldFolder)
				try
				{
					Directory.Delete(oldFolder, true);
				}
				catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
				{
					logger.LogWarning(exception, "Could not remove the previous files of beatmapset {SetId}", set.Id);
				}

			File.Move(uploadPath, Path.Combine(archiveFolder, "full.osz"), true);
			var noVideoPath = Path.Combine(archiveFolder, "novideo.osz");
			if (File.Exists(noVideoPath)) File.Delete(noVideoPath);
		}
		finally
		{
			DeleteDirectoryIfExists(unpackFolder);
			if (File.Exists(uploadPath)) File.Delete(uploadPath);
			gate.Release();
		}
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<string>> ListAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		var folder = SetFolder(set.Id.ToString(CultureInfo.InvariantCulture));
		var gate = SetLocks.GetOrAdd(set.Id, static _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken);
		try
		{
			if (!Directory.Exists(folder)) return [];
			return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
				.Select(path => Path.GetRelativePath(folder, path).Replace('\\', '/'))
				.Order(StringComparer.Ordinal)
				.ToList();
		}
		finally
		{
			gate.Release();
		}
	}

	/// <inheritdoc />
	public async Task<Stream?> OpenAsync(Beatmapset set, string name, CancellationToken cancellationToken = default)
	{
		var folder = SetFolder(set.Id.ToString(CultureInfo.InvariantCulture));
		var gate = SetLocks.GetOrAdd(set.Id, static _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken);
		try
		{
			if (!Directory.Exists(folder)) return null;

			var normalised = name.Replace('\\', '/');
			string path;
			try
			{
				path = SafePath.Combine(folder, normalised);
			}
			catch (ArgumentException)
			{
				return null;
			}

			if (File.Exists(path)) return OpenRead(path);
			return (
				from candidate in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
				where string.Equals(Path.GetRelativePath(folder, candidate).Replace('\\', '/'), normalised,
					StringComparison.OrdinalIgnoreCase)
				select OpenRead(candidate)).FirstOrDefault();
		}
		finally
		{
			gate.Release();
		}
	}

	/// <inheritdoc />
	public async Task<Stream?> OpenArchiveAsync(Beatmapset set, bool withVideo,
		CancellationToken cancellationToken = default)
	{
		var id = set.Id.ToString(CultureInfo.InvariantCulture);
		var folder = SetFolder(id);

		var gate = SetLocks.GetOrAdd(set.Id, static _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken);
		try
		{
			if (!Directory.Exists(folder)) return null;
			var archiveFolder = ArchiveFolder(id);
			Directory.CreateDirectory(archiveFolder);
			var fullPath = Path.Combine(archiveFolder, "full.osz");
			if (!File.Exists(fullPath)) BuildArchive(folder, fullPath, cancellationToken);

			if (withVideo || !HasVideo(folder)) return OpenRead(fullPath);

			var noVideoPath = Path.Combine(archiveFolder, "novideo.osz");
			if (!File.Exists(noVideoPath))
			{
				var tempPath = Path.Combine(archiveFolder, $"novideo-{Guid.NewGuid():N}.tmp");
				try
				{
					File.Copy(fullPath, tempPath);
					using (var zip = new SharpZip.ZipFile(tempPath))
					{
						zip.BeginUpdate();
						var videos = zip.Cast<SharpZip.ZipEntry>()
							.Where(entry =>
								!entry.IsDirectory && VideoExtensions.Contains(Path.GetExtension(entry.Name)))
							.ToArray();
						foreach (var entry in videos) zip.Delete(entry);
						zip.CommitUpdate();
					}

					File.Move(tempPath, noVideoPath, true);
				}
				finally
				{
					if (File.Exists(tempPath)) File.Delete(tempPath);
				}
			}

			return OpenRead(noVideoPath);
		}
		finally
		{
			gate.Release();
		}
	}

	/// <inheritdoc />
	public async Task DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		var id = set.Id.ToString(CultureInfo.InvariantCulture);
		var gate = SetLocks.GetOrAdd(set.Id, static _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken);
		try
		{
			DeleteDirectoryIfExists(SetFolder(id));
			DeleteDirectoryIfExists(ArchiveFolder(id));
		}
		finally
		{
			gate.Release();
		}
	}

	internal async Task SyncArchivesAsync(int setId, CancellationToken cancellationToken)
	{
		var id = setId.ToString(CultureInfo.InvariantCulture);
		var folder = SetFolder(id);
		var archiveFolder = ArchiveFolder(id);
		var gate = SetLocks.GetOrAdd(setId, static _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken);
		try
		{
			if (!Directory.Exists(folder))
			{
				DeleteDirectoryIfExists(archiveFolder);
				return;
			}

			if (!Directory.Exists(archiveFolder)) return;
			var allFiles = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToArray();
			var changedFiles = new HashSet<string>(StringComparer.Ordinal);
			var fullPath = Path.Combine(archiveFolder, "full.osz");
			if (File.Exists(fullPath))
				SyncArchive(fullPath, allFiles, folder, true, changedFiles, cancellationToken);

			var noVideoPath = Path.Combine(archiveFolder, "novideo.osz");
			if (File.Exists(noVideoPath))
				SyncArchive(noVideoPath, allFiles, folder, false, changedFiles, cancellationToken);

			if (!HasVideo(folder) && File.Exists(noVideoPath)) File.Delete(noVideoPath);
			foreach (var name in changedFiles.Where(name =>
				         Path.GetExtension(name).Equals(".osu", StringComparison.OrdinalIgnoreCase)))
				logger.LogWarning(
					"Beatmap file {Name} of set {SetId} changed on disk; import the set again to update its beatmaps",
					name, setId);
		}
		finally
		{
			gate.Release();
		}
	}

	internal IEnumerable<int> StoredSetIds()
	{
		if (!Directory.Exists(paths.Beatmaps)) yield break;
		foreach (var folder in Directory.EnumerateDirectories(paths.Beatmaps))
		{
			var name = Path.GetFileName(folder);
			if (name.Length > 0 && name.All(char.IsAsciiDigit) &&
			    int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
				yield return id;
		}
	}

	private static void Unpack(string archivePath, string unpackFolder, CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(unpackFolder);
		using var zip = new SharpZip.ZipFile(archivePath);
		var fileCount = 0;
		long totalSize = 0;
		var buffer = new byte[81920];
		foreach (SharpZip.ZipEntry entry in zip)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (entry.IsDirectory) continue;
			if (++fileCount > MaxFiles)
				throw new InvalidDataException($"The archive contains more than {MaxFiles} files.");
			if (entry.Size < 0 || entry.Size > MaxUnpackedBytes - totalSize)
				throw new InvalidDataException($"The archive contains more than {MaxUnpackedBytes} bytes of files.");
			totalSize += entry.Size;

			var name = entry.Name.Replace('\\', '/');
			var path = SafePath.Combine(unpackFolder, name);
			var parent = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
			using var input = zip.GetInputStream(entry);
			using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
				FileOptions.Asynchronous);
			long copied = 0;
			while (copied < entry.Size)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, entry.Size - copied));
				if (read == 0) break;
				output.Write(buffer, 0, read);
				copied += read;
			}

			if (copied != entry.Size)
				throw new InvalidDataException($"File '{name}' is shorter than its declared size.");
			if (input.ReadByte() != -1)
				throw new InvalidDataException($"File '{name}' is larger than its declared size.");
			File.SetLastWriteTime(path, entry.DateTime);
		}
	}

	private static void SyncArchive(string archivePath, string[] allFiles, string setFolder, bool includeVideo,
		HashSet<string> changedFiles, CancellationToken cancellationToken)
	{
		var files = allFiles
			.Where(path => includeVideo || !IsVideo(path))
			.Select(path => (Path: path, Name: Path.GetRelativePath(setFolder, path).Replace('\\', '/')))
			.ToArray();
		using var zip = new SharpZip.ZipFile(archivePath);
		var entries = zip.Cast<SharpZip.ZipEntry>().ToList();
		var filesByName = files.ToDictionary(file => file.Name, StringComparer.Ordinal);
		var toDelete = new List<SharpZip.ZipEntry>();
		var toAdd = new List<(string Path, string Name)>();
		foreach (var file in files)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var matches = entries.Where(entry => !entry.IsDirectory && string.Equals(entry.Name.Replace('\\', '/'),
				file.Name,
				StringComparison.Ordinal)).ToArray();
			var info = new FileInfo(file.Path);
			if (matches.Length == 1 && matches[0].Size == info.Length &&
			    Math.Abs((matches[0].DateTime - info.LastWriteTime).TotalSeconds) <= 2)
				continue;

			toDelete.AddRange(matches);
			toAdd.Add(file);
			changedFiles.Add(file.Name);
		}

		foreach (var entry in entries)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var name = entry.Name.Replace('\\', '/');
			if (filesByName.ContainsKey(name) &&
			    !toDelete.Any(candidate => ReferenceEquals(candidate, entry))) continue;
			if (!toDelete.Any(candidate => ReferenceEquals(candidate, entry))) toDelete.Add(entry);
		}

		if (toDelete.Count > 0 || toAdd.Count > 0)
		{
			zip.BeginUpdate();
			foreach (var entry in toDelete) zip.Delete(entry);
			foreach (var file in toAdd) zip.Add(file.Path, file.Name);
			zip.CommitUpdate();
		}
	}

	private static void BuildArchive(string setFolder, string archivePath, CancellationToken cancellationToken)
	{
		var tempPath = Path.Combine(Path.GetDirectoryName(archivePath)!, $"full-{Guid.NewGuid():N}.tmp");
		try
		{
			using (var zip = new SharpZip.ZipOutputStream(File.Create(tempPath)))
			{
				var buffer = new byte[81920];
				foreach (var path in Directory.EnumerateFiles(setFolder, "*", SearchOption.AllDirectories))
				{
					cancellationToken.ThrowIfCancellationRequested();
					var name = Path.GetRelativePath(setFolder, path).Replace('\\', '/');
					var info = new FileInfo(path);
					zip.PutNextEntry(new SharpZip.ZipEntry(name)
					{
						DateTime = info.LastWriteTime,
						Size = info.Length
					});
					using var input = File.OpenRead(path);
					int read;
					while ((read = input.Read(buffer, 0, buffer.Length)) > 0) zip.Write(buffer, 0, read);
				}
			}

			File.Move(tempPath, archivePath, true);
		}
		finally
		{
			if (File.Exists(tempPath)) File.Delete(tempPath);
		}
	}

	private string SetFolder(string id)
	{
		return Path.Combine(paths.Beatmaps, id);
	}

	private string ArchiveFolder(string id)
	{
		return Path.Combine(paths.BeatmapArchives, id);
	}

	private static FileStream OpenRead(string path)
	{
		return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096,
			FileOptions.Asynchronous);
	}

	private static bool HasVideo(string folder)
	{
		return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any(IsVideo);
	}

	private static bool IsVideo(string path)
	{
		return VideoExtensions.Contains(Path.GetExtension(path));
	}

	private static void DeleteDirectoryIfExists(string path)
	{
		if (Directory.Exists(path)) Directory.Delete(path, true);
	}
}