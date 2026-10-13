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

/// <summary>Stores each beatmapset's files in its own folder and exports sets as .osz archives.</summary>
internal sealed class FileBeatmapsetStorage(DataPaths paths, ILogger<FileBeatmapsetStorage> logger) : IBeatmapsetStorage
{
	private static readonly FrozenSet<string> VideoExtensions =
		new[] { ".mp4", ".avi", ".flv", ".m4v", ".mkv", ".mov", ".mpg", ".mpeg", ".webm", ".wmv" }.ToFrozenSet(
			StringComparer.OrdinalIgnoreCase);

	private static readonly ConcurrentDictionary<int, SemaphoreSlim> SetLocks = new();

	/// <inheritdoc />
	public async Task SaveAsync(Beatmapset set, IReadOnlyCollection<BeatmapsetFile> files,
		CancellationToken cancellationToken = default)
	{
		var id = set.Id.ToString(CultureInfo.InvariantCulture);
		var archiveFolder = ArchiveFolder(id);
		var setFolder = SetFolder(id);
		var unpackFolder = Path.Combine(paths.Beatmaps.Path, $"{id}.unpack-{Guid.NewGuid():N}");
		var gate = SetLocks.GetOrAdd(set.Id, static _ => new SemaphoreSlim(1, 1));

		await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await WriteAsync(unpackFolder, files, cancellationToken).ConfigureAwait(false);

			var oldFolder = Path.Combine(paths.Beatmaps.Path, $"{id}.old-{Guid.NewGuid():N}");
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

			var fullPath = Path.Combine(archiveFolder, "full.osz");
			if (File.Exists(fullPath)) File.Delete(fullPath);
			var noVideoPath = Path.Combine(archiveFolder, "novideo.osz");
			if (File.Exists(noVideoPath)) File.Delete(noVideoPath);
		}
		finally
		{
			DeleteDirectoryIfExists(unpackFolder);
			gate.Release();
		}
	}

	/// <inheritdoc />
	public async Task<IEnumerable<string>> ListAsync(Beatmapset set, CancellationToken cancellationToken = default)
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
	public async Task<Stream?> ExportAsync(Beatmapset set, bool withVideo,
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
		if (!Directory.Exists(paths.Beatmaps.Path)) yield break;
		foreach (var folder in Directory.EnumerateDirectories(paths.Beatmaps.Path))
		{
			var name = Path.GetFileName(folder);
			if (name.Length > 0 && name.All(char.IsAsciiDigit) &&
			    int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
				yield return id;
		}
	}

	/// <summary>Writes a set's files into a folder, checking that each has the size it declares.</summary>
	private static async Task WriteAsync(string folder, IReadOnlyCollection<BeatmapsetFile> files,
		CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(folder);
		var buffer = new byte[81920];
		foreach (var file in files)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string path;
			try
			{
				path = SafePath.Combine(folder, file.Name);
			}
			catch (ArgumentException exception)
			{
				throw new InvalidDataException($"File '{file.Name}' is outside the beatmapset.", exception);
			}

			var parent = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

			var input = await file.OpenAsync(cancellationToken).ConfigureAwait(false);
			await using (input.ConfigureAwait(false))
			{
				var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
					FileOptions.Asynchronous);
				await using (output.ConfigureAwait(false))
				{
					long copied = 0;
					while (copied < file.Length)
					{
						var read = await input
							.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, file.Length - copied)),
								cancellationToken).ConfigureAwait(false);
						if (read == 0) break;
						await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
						copied += read;
					}

					if (copied != file.Length)
						throw new InvalidDataException($"File '{file.Name}' is shorter than its declared size.");
					if (await input.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false) > 0)
						throw new InvalidDataException($"File '{file.Name}' is larger than its declared size.");
				}
			}
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
		return Path.Combine(paths.Beatmaps.Path, id);
	}

	private string ArchiveFolder(string id)
	{
		return Path.Combine(paths.BeatmapArchives.Path, id);
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