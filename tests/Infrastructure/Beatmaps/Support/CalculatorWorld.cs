using Basil.Domain.Game.Mechanics;
using Basil.Infrastructure.Beatmaps.Adapters;
using Basil.Infrastructure.Beatmaps.Storage;
using Basil.Testing.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Infrastructure.Beatmaps.Tests.Support;

internal sealed class CalculatorWorld : IDisposable
{
	private static readonly Dictionary<GameMode, string> Samples = new()
	{
		[GameMode.Standard] = "standard.osu", [GameMode.Taiko] = "taiko.osu", [GameMode.Catch] = "catch.osu",
		[GameMode.Mania] = "mania.osu"
	};

	private readonly TempData _data = new();

	public CalculatorWorld()
	{
		Set = AssetBeatmaps.Set();
		Storage = new FileBeatmapsetStorage(_data.Paths, TimeProvider.System,
			NullLogger<FileBeatmapsetStorage>.Instance);
		Folder = Path.Combine(_data.Paths.Beatmapsets.Path, FileBeatmapsetNames.FolderName(Set));
		Directory.CreateDirectory(Path.Combine(Folder, FileBeatmapsetNames.VersionFolder));
		File.WriteAllBytes(Path.Combine(Folder, FileBeatmapsetNames.ArchiveName), []);

		var analyser = new OsuBeatmapAnalyser();
		var id = 1;
		foreach (var (mode, file) in Samples)
		{
			var content = OszArchive.Sample(Path.Combine("Beatmaps", file));
			var hash = new Md5(content);
			File.WriteAllBytes(OsuFile(hash), content);
			using var stream = new MemoryStream(content, false);
			var analysis = analyser.Analyze(stream, mode, GameMods.NoMod);
			Beatmaps[mode] = new BeatmapRecord
			{
				Id = id++,
				Beatmapset = Set,
				Hash = hash,
				Metadata = new BeatmapMetadata
				{
					Version = mode.ToString(), Difficulty = analysis.Difficulty, ObjectCount = analysis.ObjectCount
				},
				Assets = new BeatmapAssets(),
				UpdatedAt = DateTimeOffset.UnixEpoch,
				CreatedAt = DateTimeOffset.UnixEpoch
			};
		}
	}

	public BeatmapsetRecord Set { get; }

	public FileBeatmapsetStorage Storage { get; }

	public string Folder { get; }

	public Dictionary<GameMode, BeatmapRecord> Beatmaps { get; } = [];

	public void Dispose()
	{
		_data.Dispose();
	}

	public string OsuFile(Md5 hash)
	{
		return Path.Combine(Folder, FileBeatmapsetNames.VersionFolder, $"{hash}.osu");
	}

	public string AttributesFile(GameMode mode, GameMods mask)
	{
		return FileBeatmapsetNames.AttributesPath(Folder, Beatmaps[mode].Hash, mode, mask);
	}
}
