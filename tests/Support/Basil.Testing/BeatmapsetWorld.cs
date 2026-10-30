using System.Runtime.CompilerServices;
using System.Text;
using Basil.Application.Beatmaps.Capabilities;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Common;
using Basil.Application.Common.Common;
using Basil.Domain.Beatmaps;
using Basil.Domain.Common;
using Basil.Domain.Content;
using Basil.Domain.Mechanics;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Testing;

/// <summary>
///     In-memory beatmapset storage, repositories and archive reader that behave as the port contracts document,
///     for the tests of importing and scanning beatmapsets.
/// </summary>
/// <remarks>
///     An archive is a small text document, written by <see cref="Osz" />: the reader declares it unreadable when it
///     is anything else. A difficulty's file is its text, so its MD5 hash is the MD5 of that text.
/// </remarks>
public sealed class BeatmapsetWorld
{
	private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	/// <summary>Creates the world.</summary>
	public BeatmapsetWorld()
	{
		Sets = new FakeBeatmapsets();
		Beatmaps = new FakeBeatmaps(Sets);
		Sets.Beatmaps = Beatmaps;
	}

	/// <summary>Gets the storage of the beatmapsets' archives and files.</summary>
	public FakeStorage Storage { get; } = new();

	/// <summary>Gets the stored beatmapsets.</summary>
	public FakeBeatmapsets Sets { get; }

	/// <summary>Gets the stored beatmaps.</summary>
	public FakeBeatmaps Beatmaps { get; }

	/// <summary>Replaces the beatmapset ports of an application with this world.</summary>
	/// <param name="services">The services of the application being composed.</param>
	public void Install(IServiceCollection services)
	{
		services.AddSingleton<IBeatmapsetStorage>(Storage);
		services.AddSingleton<IBeatmapsetRepository>(Sets);
		services.AddSingleton<IBeatmapRepository>(Beatmaps);
		services.AddSingleton<IBeatmapsetReader>(new FakeReader());
		services.AddSingleton<IBeatmapAnalyser>(new FakeAnalyser());
		services.AddSingleton<IBeatmapAssets>(new FakeAssets());
	}

	/// <summary>Creates an application whose beatmapset ports are this world.</summary>
	/// <returns>The application.</returns>
	public TestApplication CreateApplication()
	{
		return new TestApplication(Install);
	}

	/// <summary>Writes and reads the text form of a test archive.</summary>
	public static class Osz
	{
		/// <summary>Builds an archive of one beatmapset.</summary>
		/// <param name="onlineSetId">The beatmapset id the difficulties declare, or <see langword="null" />.</param>
		/// <param name="difficulties">The difficulties, each with its own file text.</param>
		/// <returns>The archive's bytes.</returns>
		public static byte[] Build(int? onlineSetId, params Diff[] difficulties)
		{
			var text = new StringBuilder("OSZ\n");
			text.Append("set|").Append(onlineSetId?.ToString() ?? "-").Append("|Artist|Title|Mapper\n");
			foreach (var difficulty in difficulties)
				text.Append("diff|").Append(difficulty.OnlineId?.ToString() ?? "-").Append('|')
					.Append(difficulty.Version)
					.Append('|').Append((int)difficulty.Mode).Append('|').Append(difficulty.Content).Append('\n');
			foreach (var difficulty in difficulties.Where(difficulty => difficulty.Video is not null))
				text.Append("video|").Append(difficulty.Version).Append('|').Append(difficulty.Video).Append('\n');
			return Encoding.UTF8.GetBytes(text.ToString());
		}

		/// <summary>Gets the MD5 hash of a difficulty's file.</summary>
		/// <param name="difficulty">The difficulty.</param>
		/// <returns>The hash the server takes for it.</returns>
		public static Md5 HashOf(Diff difficulty)
		{
			return new Md5(Encoding.UTF8.GetBytes(difficulty.Content));
		}
	}

	/// <summary>One difficulty of a test archive.</summary>
	/// <param name="Version">The difficulty name.</param>
	/// <param name="Content">The text of its file; difficulties of different files differ in it.</param>
	/// <param name="OnlineId">The beatmap id it declares, or <see langword="null" />.</param>
	/// <param name="Mode">The game mode.</param>
	/// <param name="Video">The name of the video file the difficulty declares, or <see langword="null" />.</param>
	public sealed record Diff(
		string Version,
		string Content,
		int? OnlineId = null,
		GameMode Mode = GameMode.Standard,
		string? Video = null);

	/// <summary>An archive held in memory.</summary>
	public sealed class MemoryArchive(string name, byte[] bytes, DateTimeOffset modifiedAt)
		: BeatmapsetArchive(name, new ContentStamp(bytes.Length, modifiedAt))
	{
		/// <summary>Gets the archive's bytes.</summary>
		public byte[] Bytes => bytes;

		/// <summary>Gets how many times the archive was opened.</summary>
		public int Opened { get; private set; }

		/// <inheritdoc />
		public override Task<Stream> OpenAsync(CancellationToken cancellationToken = default)
		{
			Opened++;
			return Task.FromResult<Stream>(new MemoryStream(bytes, false));
		}
	}

	/// <summary>A folder held in memory.</summary>
	public sealed class MemoryFolder(
		string name,
		int? id,
		bool exists,
		BeatmapsetArchive? archive,
		byte[]? looseFiles = null)
		: BeatmapsetFolder(name, id, exists, archive)
	{
		/// <summary>Gets what the folder's loose files pack into, or <see langword="null" /> when it packs to nothing.</summary>
		public byte[]? LooseFiles => looseFiles;
	}

	/// <summary>An in-memory beatmapset storage.</summary>
	public sealed class FakeStorage : IBeatmapsetStorage
	{
		private readonly List<MemoryArchive> _uploaded = [];
		private readonly List<BeatmapsetSource> _waiting = [];
		private int _uploads;

		/// <summary>Gets the archive and the files stored for each stored beatmapset.</summary>
		public Dictionary<int, StoredSet> Stored { get; } = [];

		/// <summary>Gets the uploaded or packed archives that are still held.</summary>
		public IReadOnlyList<MemoryArchive> Uploaded => _uploaded;

		/// <summary>Gets the sources placed in the storage by hand and not yet taken in.</summary>
		public IReadOnlyList<BeatmapsetSource> Waiting => _waiting;

		/// <inheritdoc />
		public Task<BeatmapsetArchive> SaveAsync(Stream archive, CancellationToken cancellationToken = default)
		{
			using var copy = new MemoryStream();
			archive.CopyTo(copy);
			var saved = new MemoryArchive($"upload-{++_uploads}", copy.ToArray(), Epoch);
			_uploaded.Add(saved);
			return Task.FromResult<BeatmapsetArchive>(saved);
		}

		/// <inheritdoc />
		public Task SaveAsync(BeatmapsetRecord set, BeatmapsetArchive archive,
			CancellationToken cancellationToken = default)
		{
			var memory = (MemoryArchive)archive;
			_uploaded.Remove(memory);
			_waiting.Remove(memory);
			Stored[set.Id] = new StoredSet { Archive = memory };
			return Task.CompletedTask;
		}

		/// <inheritdoc />
		public Task<BeatmapsetArchive?> PackAsync(BeatmapsetFolder folder,
			CancellationToken cancellationToken = default)
		{
			if (folder is not MemoryFolder { Exists: true, LooseFiles: { } files })
				return Task.FromResult<BeatmapsetArchive?>(null);
			var packed = new MemoryArchive($"packed-{++_uploads}", files, Epoch);
			_uploaded.Add(packed);
			_waiting.Remove(folder);
			return Task.FromResult<BeatmapsetArchive?>(packed);
		}

		/// <inheritdoc />
		public Task ClearAsync(BeatmapsetRecord set, CancellationToken cancellationToken = default)
		{
			if (Stored.TryGetValue(set.Id, out var stored))
			{
				stored.Files.Clear();
				stored.WithoutVideo = false;
			}

			return Task.CompletedTask;
		}

		/// <inheritdoc />
		public Task SaveAsync(BeatmapsetRecord set, BeatmapsetFileType type, string name, Stream content,
			CancellationToken cancellationToken = default)
		{
			Stored[set.Id].Files.Add((type, name));
			return Task.CompletedTask;
		}

		/// <inheritdoc />
		public Task SaveArchiveWithoutVideoAsync(BeatmapsetRecord set, IReadOnlyCollection<string> videoFiles,
			CancellationToken cancellationToken = default)
		{
			Stored[set.Id].WithoutVideo = true;
			Stored[set.Id].VideoFiles = [.. videoFiles];
			return Task.CompletedTask;
		}

		/// <inheritdoc />
		public Task<bool> ExistsAsync(BeatmapsetRecord set, BeatmapsetFileType type, string name,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(Stored.TryGetValue(set.Id, out var stored) && stored.Files.Contains((type, name)));
		}

		/// <inheritdoc />
		public Task<Stream?> OpenAsync(BeatmapsetRecord set, BeatmapsetFileType type, string name,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult<Stream?>(null);
		}

		/// <inheritdoc />
		public Task<Stream?> OpenArchiveAsync(BeatmapsetRecord set, bool withVideo,
			CancellationToken cancellationToken = default)
		{
			if (!Stored.TryGetValue(set.Id, out var stored) || (!withVideo && !stored.WithoutVideo))
				return Task.FromResult<Stream?>(null);
			return Task.FromResult<Stream?>(new MemoryStream(stored.Archive.Bytes, false));
		}

		/// <inheritdoc />
		public Task<IEnumerable<BeatmapsetSource>> ListAsync(CancellationToken cancellationToken = default)
		{
			IEnumerable<BeatmapsetSource> all = _waiting.ToList().Concat(Stored.Keys.Select(FolderOf)).ToList();
			return Task.FromResult(all);
		}

		/// <inheritdoc />
		public async IAsyncEnumerable<BeatmapsetSource> WatchAsync(
			[EnumeratorCancellation] CancellationToken cancellationToken)
		{
			await Task.CompletedTask;
			yield break;
		}

		/// <inheritdoc />
		public Task DeleteAsync(BeatmapsetRecord set, CancellationToken cancellationToken = default)
		{
			Stored.Remove(set.Id);
			return Task.CompletedTask;
		}

		/// <inheritdoc />
		public Task DeleteAsync(BeatmapsetArchive archive, CancellationToken cancellationToken = default)
		{
			_uploaded.Remove((MemoryArchive)archive);
			return Task.CompletedTask;
		}

		/// <summary>Places an archive in the storage, as someone would by hand.</summary>
		/// <param name="name">The archive's name.</param>
		/// <param name="bytes">The archive's bytes.</param>
		/// <param name="modifiedAt">When it was modified.</param>
		/// <returns>The archive.</returns>
		public MemoryArchive PlaceArchive(string name, byte[] bytes, DateTimeOffset? modifiedAt = null)
		{
			var archive = new MemoryArchive(name, bytes, modifiedAt ?? Epoch);
			_waiting.Add(archive);
			return archive;
		}

		/// <summary>Places a folder of loose files in the storage, as someone would by hand.</summary>
		/// <param name="name">The folder's name.</param>
		/// <param name="packsTo">What its files pack into.</param>
		/// <param name="id">The id its name starts with, or <see langword="null" />.</param>
		/// <returns>The folder.</returns>
		public MemoryFolder PlaceLooseFolder(string name, byte[] packsTo, int? id = null)
		{
			var folder = new MemoryFolder(name, id, true, null, packsTo);
			_waiting.Add(folder);
			return folder;
		}

		/// <summary>Replaces the archive a stored set's folder holds, as an edit by hand would.</summary>
		/// <param name="id">The set's id.</param>
		/// <param name="bytes">The new bytes.</param>
		/// <param name="modifiedAt">The new modification time.</param>
		public void Overwrite(int id, byte[] bytes, DateTimeOffset modifiedAt)
		{
			Stored[id].Archive = new MemoryArchive($"{id}", bytes, modifiedAt);
		}

		/// <summary>Removes a stored set's folder with its archive and files.</summary>
		/// <param name="id">The set's id.</param>
		public void RemoveFolder(int id)
		{
			Stored.Remove(id);
		}

		/// <summary>Gets the folder entry of a stored set, as the storage lists it.</summary>
		/// <param name="id">The set's id.</param>
		/// <returns>The folder.</returns>
		public MemoryFolder FolderOf(int id)
		{
			return new MemoryFolder($"{id}", id, Stored.ContainsKey(id), Stored.GetValueOrDefault(id)?.Archive);
		}
	}

	/// <summary>What the storage holds for one stored beatmapset.</summary>
	public sealed class StoredSet
	{
		/// <summary>Gets the set's archive.</summary>
		public required MemoryArchive Archive { get; set; }

		/// <summary>Gets the files made from the archive.</summary>
		public HashSet<(BeatmapsetFileType Type, string Name)> Files { get; } = [];

		/// <summary>Gets a value that indicates whether the archive without video is made.</summary>
		public bool WithoutVideo { get; set; }

		/// <summary>Gets the names of the video files the archive without video was made without.</summary>
		public IReadOnlyList<string> VideoFiles { get; set; } = [];
	}

	/// <summary>In-memory beatmapset records.</summary>
	public sealed class FakeBeatmapsets : IBeatmapsetRepository
	{
		private int _nextLocalId = 1_000_000_000;

		/// <summary>Gets or sets the beatmap records, which a commit changes with the sets.</summary>
		internal FakeBeatmaps Beatmaps { get; set; } = null!;

		/// <summary>Gets the stored sets by id.</summary>
		public Dictionary<int, BeatmapsetRecord> Records { get; } = [];

		/// <inheritdoc />
		public ValueTask<BeatmapsetRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
		{
			return ValueTask.FromResult(Records.GetValueOrDefault(id));
		}

		/// <inheritdoc />
		public Task UpdateAsync(BeatmapsetRecord set, CancellationToken cancellationToken = default)
		{
			Records[set.Id] = set;
			return Task.CompletedTask;
		}

		/// <inheritdoc />
		public Task<Page<BeatmapsetRecord>> ListAsync(BeatmapQuery query, PageRequest page,
			CancellationToken cancellationToken = default)
		{
			var all = Records.Values.OrderByDescending(set => set.Id).ToList();
			return Task.FromResult(new Page<BeatmapsetRecord>(all.Skip(page.Offset).Take(page.Limit).ToList(),
				all.Count));
		}

		/// <inheritdoc />
		public Task<IReadOnlyList<BeatmapsetRecord>> CommitAsync(BeatmapsetChanges changes,
			CancellationToken cancellationToken = default)
		{
			var stored = new List<BeatmapsetRecord>();
			foreach (var change in changes.Store)
			{
				BeatmapsetRecord set;
				if (change.Existing is { } existing)
				{
					set = Records[existing.Id];
					set.Fingerprint = change.Data.Fingerprint;
					set.Metadata = change.Data.Metadata;
					set.Assets = change.Data.Assets;
					set.UpdatedAt = Epoch;
				}
				else
				{
					var id = change.Data.OnlineId ?? NewLocalId();
					if (Records.ContainsKey(id)) throw new AlreadyExistsException("The beatmapset id is taken.");
					set = new BeatmapsetRecord
					{
						Id = id, Fingerprint = change.Data.Fingerprint, Metadata = change.Data.Metadata,
						Assets = change.Data.Assets, CreatedAt = Epoch, UpdatedAt = Epoch
					};
					Records[id] = set;
				}

				Beatmaps.Replace(set, change.Data.Beatmaps);
				stored.Add(set);
			}

			foreach (var removed in changes.Remove)
			{
				Beatmaps.RemoveAllOf(removed);
				Records.Remove(removed.Id);
			}

			return Task.FromResult<IReadOnlyList<BeatmapsetRecord>>(stored);
		}

		/// <inheritdoc />
		public Task DeleteAsync(BeatmapsetRecord set, CancellationToken cancellationToken = default)
		{
			Records.Remove(set.Id);
			return Task.CompletedTask;
		}

		/// <summary>Allocates a new local id.</summary>
		/// <returns>The id.</returns>
		internal int NewLocalId()
		{
			return _nextLocalId++;
		}
	}

	/// <summary>In-memory beatmap records.</summary>
	public sealed class FakeBeatmaps(FakeBeatmapsets sets) : IBeatmapRepository
	{
		/// <summary>Gets the stored beatmaps by hash.</summary>
		public Dictionary<Md5, BeatmapRecord> Records { get; } = [];

		/// <inheritdoc />
		public Task UpdateAsync(BeatmapRecord beatmap, CancellationToken cancellationToken = default)
		{
			Records[beatmap.Hash] = beatmap;
			return Task.CompletedTask;
		}

		/// <inheritdoc />
		public ValueTask<BeatmapRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
		{
			return ValueTask.FromResult(Records.Values.FirstOrDefault(beatmap => beatmap.Id == id));
		}

		/// <inheritdoc />
		public ValueTask<BeatmapRecord?> GetAsync(Md5 hash, CancellationToken cancellationToken = default)
		{
			return ValueTask.FromResult(Records.GetValueOrDefault(hash));
		}

		/// <inheritdoc />
		public Task<IReadOnlyList<BeatmapRecord>> ListAsync(BeatmapsetRecord set,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult<IReadOnlyList<BeatmapRecord>>(Records.Values
				.Where(beatmap => beatmap.Beatmapset.Equals(set)).OrderBy(beatmap => beatmap.Id).ToList());
		}

		/// <inheritdoc />
		public Task<Page<BeatmapRecord>> ListAsync(BeatmapQuery query, PageRequest page,
			CancellationToken cancellationToken = default)
		{
			var all = Records.Values.OrderByDescending(beatmap => beatmap.Beatmapset.Id).ToList();
			return Task.FromResult(new Page<BeatmapRecord>(all.Skip(page.Offset).Take(page.Limit).ToList(), all.Count));
		}

		/// <inheritdoc />
		public Task RetainAsync(BeatmapsetRecord set, IReadOnlyCollection<BeatmapRecord> keep,
			CancellationToken cancellationToken = default)
		{
			foreach (var hash in Records.Where(pair => pair.Value.Beatmapset.Equals(set) && !keep.Contains(pair.Value))
				         .Select(pair => pair.Key).ToList())
				Records.Remove(hash);
			return Task.CompletedTask;
		}

		/// <summary>Stores the difficulties a set now has and removes the ones it no longer has.</summary>
		/// <param name="set">The set.</param>
		/// <param name="data">Every difficulty the set now has.</param>
		internal void Replace(BeatmapsetRecord set, IReadOnlyList<BeatmapData> data)
		{
			var keep = new HashSet<Md5>();
			foreach (var item in data)
			{
				var known = Records.GetValueOrDefault(item.Hash) ??
				            (item.OnlineId is { } id
					            ? Records.Values.FirstOrDefault(beatmap =>
						            beatmap.Id == id && beatmap.Beatmapset.Equals(set))
					            : null);
				if (known is not null)
				{
					Records.Remove(known.Hash);
					known.Beatmapset = set;
					known.Hash = item.Hash;
					known.Metadata = item.Metadata;
					known.Assets = item.Assets;
					known.UpdatedAt = Epoch;
					Records[item.Hash] = known;
				}
				else
				{
					Records[item.Hash] = new BeatmapRecord
					{
						Id = item.OnlineId ?? sets.NewLocalId(), Beatmapset = set, Hash = item.Hash,
						Metadata = item.Metadata, Assets = item.Assets, CreatedAt = Epoch, UpdatedAt = Epoch
					};
				}

				keep.Add(item.Hash);
			}

			foreach (var hash in Records.Where(pair => pair.Value.Beatmapset.Equals(set) && !keep.Contains(pair.Key))
				         .Select(pair => pair.Key).ToList())
				Records.Remove(hash);
		}

		/// <summary>Removes every beatmap of a set.</summary>
		/// <param name="set">The set.</param>
		internal void RemoveAllOf(BeatmapsetRecord set)
		{
			foreach (var hash in Records.Where(pair => pair.Value.Beatmapset.Equals(set)).Select(pair => pair.Key)
				         .ToList())
				Records.Remove(hash);
		}
	}

	private sealed class FakeReader : IBeatmapsetReader
	{
		public async Task<BeatmapsetContent?> ReadAsync(Stream archive, CancellationToken cancellationToken = default)
		{
			using var reader = new StreamReader(archive, Encoding.UTF8, false, 1024, true);
			var text = await reader.ReadToEndAsync(cancellationToken);
			var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
			if (lines.Length < 2 || lines[0] != "OSZ" ||
			    !lines[1].StartsWith("set|", StringComparison.Ordinal)) return null;

			var set = lines[1].Split('|');
			var difficulties = new List<BeatmapsetDifficulty>();
			var files = new List<BeatmapsetContentFile>();
			var entries = new Dictionary<string, MemoryFile>();
			var videos = lines.Skip(2).Where(line => line.StartsWith("video|", StringComparison.Ordinal))
				.Select(line => line.Split('|', 3)).ToDictionary(part => part[1], part => part[2]);
			foreach (var line in lines.Skip(2).Where(line => !line.StartsWith("video|", StringComparison.Ordinal)))
			{
				var part = line.Split('|', 5);
				var file = new MemoryFile($"{part[2]}.osu", Encoding.UTF8.GetBytes(part[4]));
				files.Add(file);
				MemoryFile? video = null;
				if (videos.TryGetValue(part[2], out var videoName) && !entries.TryGetValue(videoName, out video))
				{
					// One file of the archive is one object, however many difficulties declare it.
					entries[videoName] = video = new MemoryFile(videoName, [1]);
					files.Add(video);
				}

				difficulties.Add(new BeatmapsetDifficulty(part[1] == "-" ? null : int.Parse(part[1]), part[2],
					(GameMode)int.Parse(part[3]), file, null, null, video, null, false));
			}

			return difficulties.Count == 0
				? null
				: new BeatmapsetContent(set[1] == "-" ? null : int.Parse(set[1]), set[2], set[3], set[4], difficulties,
					files, null, null);
		}
	}

	private sealed class MemoryFile(string name, byte[] bytes) : BeatmapsetContentFile(name, bytes.Length)
	{
		public override Task<Stream> OpenAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult<Stream>(new MemoryStream(bytes, false));
		}
	}

	private sealed class FakeAnalyser : IBeatmapAnalyser
	{
		public BeatmapAnalysis Analyze(Stream content, GameMode mode, GameMods mods)
		{
			return new BeatmapAnalysis(new BeatmapDifficulty(mode, 120, TimeSpan.FromMinutes(1), 4, 5, 5, 5, 2.5),
				BeatmapObjects.Empty(mode));
		}

		public Task<BeatmapAnalysis?> AnalyzeAsync(BeatmapRecord beatmap, GameMods mods,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult<BeatmapAnalysis?>(null);
		}
	}

	private sealed class FakeAssets : IBeatmapAssets
	{
		public Task BuildAsync(BeatmapsetRecord set, IReadOnlyList<BeatmapRecord> beatmaps,
			CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}
	}
}