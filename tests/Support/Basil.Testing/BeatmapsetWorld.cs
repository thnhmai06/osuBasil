using System.Runtime.CompilerServices;
using System.Text;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Ports;
using Basil.Application.Beatmaps.Storage;
using Basil.Application.Shared.Exceptions;
using Basil.Application.Shared.Paging;
using Basil.Domain.Beatmaps;
using Basil.Domain.Content;
using Basil.Domain.Cryptography;
using Basil.Domain.Mechanics;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Testing;

public sealed class BeatmapsetWorld
{
	private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	public BeatmapsetWorld()
	{
		Sets = new FakeBeatmapsets();
		Beatmaps = new FakeBeatmaps(Sets);
		Sets.Beatmaps = Beatmaps;
	}

	public FakeStorage Storage { get; } = new();

	public FakeBeatmapsets Sets { get; }

	public FakeBeatmaps Beatmaps { get; }

	public void Install(IServiceCollection services)
	{
		services.AddSingleton<IBeatmapsetStorage>(Storage);
		services.AddSingleton<IBeatmapsetRepository>(Sets);
		services.AddSingleton<IBeatmapRepository>(Beatmaps);
		services.AddSingleton<IBeatmapsetReader>(new FakeReader());
		services.AddSingleton<IBeatmapAnalyser>(new FakeAnalyser());
		services.AddSingleton<IBeatmapAssets>(new FakeAssets());
	}

	public TestApplication CreateApplication()
	{
		return new TestApplication(Install);
	}

	public static class Osz
	{
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

		public static Md5 HashOf(Diff difficulty)
		{
			return new Md5(Encoding.UTF8.GetBytes(difficulty.Content));
		}
	}

	public sealed record Diff(
		string Version,
		string Content,
		int? OnlineId = null,
		GameMode Mode = GameMode.Standard,
		string? Video = null);

	public sealed class MemoryArchive(string name, byte[] bytes, DateTimeOffset modifiedAt)
		: BeatmapsetArchive(name, new ContentStamp(bytes.Length, modifiedAt))
	{
		public byte[] Bytes => bytes;

		public int Opened { get; private set; }

		public override Task<Stream> OpenAsync(CancellationToken ct = default)
		{
			Opened++;
			return Task.FromResult<Stream>(new MemoryStream(bytes, false));
		}
	}

	public sealed class MemoryFolder(
		string name,
		int? id,
		bool exists,
		BeatmapsetArchive? archive,
		byte[]? looseFiles = null)
		: BeatmapsetFolder(name, id, exists, archive)
	{
		public byte[]? LooseFiles => looseFiles;
	}

	public sealed class FakeStorage : IBeatmapsetStorage
	{
		private readonly List<MemoryArchive> _uploaded = [];
		private readonly List<BeatmapsetSource> _waiting = [];
		private int _uploads;

		public Dictionary<int, StoredSet> Stored { get; } = [];

		public IReadOnlyList<MemoryArchive> Uploaded => _uploaded;

		public IReadOnlyList<BeatmapsetSource> Waiting => _waiting;

		public List<MemoryArchive> Quarantined { get; } = [];

		public Task<BeatmapsetArchive> SaveAsync(Stream archive, CancellationToken ct = default)
		{
			using var copy = new MemoryStream();
			archive.CopyTo(copy);
			var saved = new MemoryArchive($"upload-{++_uploads}", copy.ToArray(), Epoch);
			_uploaded.Add(saved);
			return Task.FromResult<BeatmapsetArchive>(saved);
		}

		public Task SaveAsync(BeatmapsetRecord set, BeatmapsetArchive archive)
		{
			var memory = (MemoryArchive)archive;
			_uploaded.Remove(memory);
			_waiting.Remove(memory);
			Stored[set.Id] = new StoredSet { Archive = memory };
			return Task.CompletedTask;
		}

		public Task<BeatmapsetArchive?> PackAsync(BeatmapsetFolder folder,
			CancellationToken ct = default)
		{
			if (folder is not MemoryFolder { Exists: true, LooseFiles: { } files })
				return Task.FromResult<BeatmapsetArchive?>(null);
			var packed = new MemoryArchive($"packed-{++_uploads}", files, Epoch);
			_uploaded.Add(packed);
			_waiting.Remove(folder);
			return Task.FromResult<BeatmapsetArchive?>(packed);
		}

		public Task ClearAsync(BeatmapsetRecord set)
		{
			if (Stored.TryGetValue(set.Id, out var stored))
			{
				stored.Files.Clear();
				stored.WithoutVideo = false;
			}

			return Task.CompletedTask;
		}

		public Task SaveAsync(BeatmapsetRecord set, BeatmapsetFileType type, string name, Stream content)
		{
			Stored[set.Id].Files.Add((type, name));
			return Task.CompletedTask;
		}

		public Task SaveArchiveWithoutVideoAsync(BeatmapsetRecord set, IReadOnlyCollection<string> videoFiles)
		{
			Stored[set.Id].WithoutVideo = true;
			Stored[set.Id].VideoFiles = [.. videoFiles];
			return Task.CompletedTask;
		}

		public Task<bool> ExistsAsync(BeatmapsetRecord set, BeatmapsetFileType type, string name,
			CancellationToken ct = default)
		{
			return Task.FromResult(Stored.TryGetValue(set.Id, out var stored) && stored.Files.Contains((type, name)));
		}

		public Task<Stream?> OpenAsync(BeatmapsetRecord set, BeatmapsetFileType type, string name,
			CancellationToken ct = default)
		{
			return Task.FromResult<Stream?>(null);
		}

		public Task<Stream?> OpenArchiveAsync(BeatmapsetRecord set, bool withVideo,
			CancellationToken ct = default)
		{
			if (!Stored.TryGetValue(set.Id, out var stored) || (!withVideo && !stored.WithoutVideo))
				return Task.FromResult<Stream?>(null);
			return Task.FromResult<Stream?>(new MemoryStream(stored.Archive.Bytes, false));
		}

		public Task<IEnumerable<BeatmapsetSource>> ListAsync(CancellationToken ct = default)
		{
			IEnumerable<BeatmapsetSource> all = _waiting.ToList().Concat(Stored.Keys.Select(FolderOf)).ToList();
			return Task.FromResult(all);
		}

		public async IAsyncEnumerable<BeatmapsetSource> WatchAsync(
			[EnumeratorCancellation] CancellationToken ct)
		{
			await Task.CompletedTask;
			yield break;
		}

		public Task DeleteAsync(BeatmapsetRecord set)
		{
			Stored.Remove(set.Id);
			return Task.CompletedTask;
		}

		public Task QuarantineAsync(BeatmapsetArchive archive)
		{
			_uploaded.Remove((MemoryArchive)archive);
			Quarantined.Add((MemoryArchive)archive);
			return Task.CompletedTask;
		}

		public Task DeleteAsync(BeatmapsetArchive archive)
		{
			_uploaded.Remove((MemoryArchive)archive);
			return Task.CompletedTask;
		}

		public MemoryArchive PlaceArchive(string name, byte[] bytes, DateTimeOffset? modifiedAt = null)
		{
			var archive = new MemoryArchive(name, bytes, modifiedAt ?? Epoch);
			_waiting.Add(archive);
			return archive;
		}

		public MemoryFolder PlaceLooseFolder(string name, byte[] packsTo, int? id = null)
		{
			var folder = new MemoryFolder(name, id, true, null, packsTo);
			_waiting.Add(folder);
			return folder;
		}

		public void Overwrite(int id, byte[] bytes, DateTimeOffset modifiedAt)
		{
			Stored[id].Archive = new MemoryArchive($"{id}", bytes, modifiedAt);
		}

		public void RemoveFolder(int id)
		{
			Stored.Remove(id);
		}

		public MemoryFolder FolderOf(int id)
		{
			return new MemoryFolder($"{id}", id, Stored.ContainsKey(id), Stored.GetValueOrDefault(id)?.Archive);
		}
	}

	public sealed class StoredSet
	{
		public required MemoryArchive Archive { get; set; }

		public HashSet<(BeatmapsetFileType Type, string Name)> Files { get; } = [];

		public bool WithoutVideo { get; set; }

		public IReadOnlyList<string> VideoFiles { get; set; } = [];
	}

	public sealed class FakeBeatmapsets : IBeatmapsetRepository
	{
		private int _nextLocalId = 1_000_000_000;

		internal FakeBeatmaps Beatmaps { get; set; } = null!;

		public Dictionary<int, BeatmapsetRecord> Records { get; } = [];

		public Exception? UpdateFailure { get; set; }

		public ValueTask<BeatmapsetRecord?> GetAsync(int id, CancellationToken ct = default)
		{
			return ValueTask.FromResult(Records.GetValueOrDefault(id));
		}

		public Task UpdateAsync(BeatmapsetRecord set)
		{
			if (UpdateFailure is { } failure) throw failure;
			Records[set.Id] = set;
			return Task.CompletedTask;
		}

		public Task<Page<BeatmapsetRecord>> ListAsync(BeatmapQuery query, PageRequest page,
			CancellationToken ct = default)
		{
			var all = Records.Values.OrderByDescending(set => set.Id).ToList();
			return Task.FromResult(new Page<BeatmapsetRecord>(all.Skip(page.Offset).Take(page.Limit).ToList(),
				page.Offset + page.Limit < all.Count));
		}

		public Task<IReadOnlyList<int>> ListIdsAsync(CancellationToken ct = default)
		{
			return Task.FromResult<IReadOnlyList<int>>([.. Records.Keys.OrderByDescending(id => id)]);
		}

		public Task<IReadOnlyList<BeatmapsetRecord>> CommitAsync(BeatmapsetChanges changes)
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

		public Task DeleteAsync(BeatmapsetRecord set)
		{
			Records.Remove(set.Id);
			return Task.CompletedTask;
		}

		internal int NewLocalId()
		{
			return _nextLocalId++;
		}
	}

	public sealed class FakeBeatmaps(FakeBeatmapsets sets) : IBeatmapRepository
	{
		public Dictionary<Md5, BeatmapRecord> Records { get; } = [];

		public Task UpdateAsync(BeatmapRecord beatmap)
		{
			Records[beatmap.Hash] = beatmap;
			return Task.CompletedTask;
		}

		public ValueTask<BeatmapRecord?> GetAsync(int id, CancellationToken ct = default)
		{
			return ValueTask.FromResult(Records.Values.FirstOrDefault(beatmap => beatmap.Id == id));
		}

		public ValueTask<BeatmapRecord?> GetAsync(Md5 hash, CancellationToken ct = default)
		{
			return ValueTask.FromResult(Records.GetValueOrDefault(hash));
		}

		public Task<IReadOnlyList<BeatmapRecord>> ListAsync(BeatmapsetRecord set,
			CancellationToken ct = default)
		{
			return Task.FromResult<IReadOnlyList<BeatmapRecord>>(Records.Values
				.Where(beatmap => beatmap.Beatmapset.Equals(set)).OrderBy(beatmap => beatmap.Id).ToList());
		}

		public Task<Page<BeatmapRecord>> ListAsync(BeatmapQuery query, PageRequest page,
			CancellationToken ct = default)
		{
			var all = Records.Values.OrderByDescending(beatmap => beatmap.Beatmapset.Id).ToList();
			return Task.FromResult(new Page<BeatmapRecord>(all.Skip(page.Offset).Take(page.Limit).ToList(),
				page.Offset + page.Limit < all.Count));
		}

		public Task RetainAsync(BeatmapsetRecord set, IReadOnlyCollection<BeatmapRecord> keep)
		{
			foreach (var hash in Records.Where(pair => pair.Value.Beatmapset.Equals(set) && !keep.Contains(pair.Value))
				         .Select(pair => pair.Key).ToList())
				Records.Remove(hash);
			return Task.CompletedTask;
		}

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

		internal void RemoveAllOf(BeatmapsetRecord set)
		{
			foreach (var hash in Records.Where(pair => pair.Value.Beatmapset.Equals(set)).Select(pair => pair.Key)
				         .ToList())
				Records.Remove(hash);
		}
	}

	private sealed class FakeReader : IBeatmapsetReader
	{
		public async Task<BeatmapsetContent?> ReadAsync(Stream archive, CancellationToken ct = default)
		{
			using var reader = new StreamReader(archive, Encoding.UTF8, false, 1024, true);
			var text = await reader.ReadToEndAsync(ct);
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
		public override Task<Stream> OpenAsync(CancellationToken ct = default)
		{
			return Task.FromResult<Stream>(new MemoryStream(bytes, false));
		}
	}

	private sealed class FakeAnalyser : IBeatmapAnalyser
	{
		public BeatmapAnalysis Analyze(Stream content, GameMode mode, GameMods mods)
		{
			return new BeatmapAnalysis(new BeatmapDifficulty(mode, 120, TimeSpan.FromMinutes(1), 4, 5, 5, 5, 2.5),
				BeatmapObjectCount.Empty(mode));
		}
	}

	private sealed class FakeAssets : IBeatmapAssets
	{
		public Task BuildAsync(BeatmapsetRecord set, IReadOnlyList<BeatmapRecord> beatmaps,
			CancellationToken ct = default)
		{
			return Task.CompletedTask;
		}
	}
}