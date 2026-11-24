using Basil.Application.Rulesets.Ports;
using Basil.Domain.Game;
using Basil.Domain.Scores;
using Basil.Infrastructure.Beatmaps.Adapters;
using Basil.Infrastructure.Rulesets.Adapters;
using Basil.Infrastructure.Rulesets.Storage;
using Basil.Infrastructure.Shared.Storage;
using Basil.Shared.Structures.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManifestTool;

/// <summary>Runs what the server runs on the osu! stack: the beatmap reader, the difficulty and pp calculators, the attribute files.</summary>
internal static class Trial
{
	private static readonly GameMods[] Mods =
	[
		GameMods.NoMod, GameMods.DoubleTime, GameMods.HardRock, GameMods.Easy, GameMods.HalfTime,
		GameMods.Hidden | GameMods.Flashlight, GameMods.DoubleTime | GameMods.HardRock, GameMods.SpunOut,
		GameMods.NoFail
	];

	public static async Task RunAsync(string assets)
	{
		var beatmaps = Path.Combine(assets, "Beatmaps");
		var files = new[] { "standard", "taiko", "catch", "mania" }.Select(name => Path.Combine(beatmaps, name + ".osu")).ToArray();

		// The stack is not checked here: this run is what finds out which files the stack needs.
		var paths = new DataPaths();
		var store = new RulesetStore(paths, NullLogger<RulesetStore>.Instance);
		var calculator = new OsuBeatmapCalculator(new Files(files), new AttributeStorage(paths), store, NullLogger<OsuBeatmapCalculator>.Instance);

		for (var index = 0; index < files.Length; index++)
		foreach (var mode in Enum.GetValues<GameMode>())
		foreach (var mods in Mods)
		{
			await using var stream = File.OpenRead(files[index]);
			// Every beatmap is also converted to the other modes.
			var analysis = OsuBeatmapAnalyser.AnalyzeInstalled(stream, mode, mods);
			if (!double.IsFinite(analysis.Difficulty.Star)) throw new InvalidOperationException("No star rating.");
		}

		var hits = new HitCounts(90, 8, 1, 10, 3, 2);
		for (var round = 0; round < 2; round++) // the second round reads the attribute files written by the first
		for (var index = 0; index < files.Length; index++)
		foreach (var mods in Mods)
		{
			var mode = (GameMode)index;
			var checksum = Md5Hash.Parse(index.ToString("x32"));
			_ = await calculator.StarsAsync(index, checksum, mode, mods, default) ?? throw new InvalidOperationException("No stars.");
			_ = await calculator.PerformanceAsync(index, checksum, mode, mods, hits, 50, default) ?? throw new InvalidOperationException("No pp.");
		}

		var reader = new OsuBeatmapsetReader();
		foreach (var set in Directory.GetFiles(Path.Combine(assets, "Sets"), "*.osz"))
		{
			await using var stream = File.OpenRead(set);
			await using var content = await reader.ReadAsync(stream) ?? throw new InvalidOperationException($"{set} was not read.");
		}

		// A malformed difficulty is skipped, not fatal.
		using var broken = new MemoryStream();
		using (var zip = new System.IO.Compression.ZipArchive(broken, System.IO.Compression.ZipArchiveMode.Create, true))
		await using (var entry = zip.CreateEntry("broken.osu").Open())
			await entry.WriteAsync("osu file format v14\n[General]\nnot a beatmap"u8.ToArray());

		broken.Position = 0;
		_ = await reader.ReadAsync(broken);
	}

	/// <summary>Runs in a copy that holds no osu! assembly: the stack is missing, then installed from <paramref name="stack"/>.</summary>
	public static async Task<int> RunOffAsync(string stack, string assets)
	{
		var paths = new DataPaths();
		var store = new RulesetStore(paths, NullLogger<RulesetStore>.Instance);
		store.Initialize();
		var file = Path.Combine(assets, "Beatmaps", "standard.osu");
		var analyser = new OsuBeatmapAnalyser(store);
		var calculator = new OsuBeatmapCalculator(new Files([file]), new AttributeStorage(paths), store, NullLogger<OsuBeatmapCalculator>.Instance);
		var checksum = Md5Hash.Parse(0.ToString("x32"));

		await using (var stream = File.OpenRead(file))
			if (store.State != Basil.Application.Rulesets.Services.RulesetState.Missing || analyser.Analyze(stream, GameMode.Standard, GameMods.NoMod) is not null ||
			    await calculator.GetStarsAsync(0, checksum, GameMode.Standard, GameMods.NoMod) is not null ||
			    await calculator.GetPerformanceAsync(0, checksum, GameMode.Standard, GameMods.NoMod, default, 1) is not null)
				return 1;

		Directory.CreateDirectory(store.Folder);
		foreach (var source in Directory.GetFiles(stack)) File.Copy(source, Path.Combine(store.Folder, Path.GetFileName(source)));
		if (store.Check() != Basil.Application.Rulesets.Services.RulesetState.Installed) return 2;

		await using var again = File.OpenRead(file);
		if (analyser.Analyze(again, GameMode.Standard, GameMods.NoMod) is null ||
		    await calculator.GetStarsAsync(0, checksum, GameMode.Standard, GameMods.NoMod) is null) return 3;

		return 0;
	}

	private sealed class Files(string[] paths) : IBeatmapFiles
	{
		public ValueTask<Stream?> OpenAsync(int beatmapId, Md5Hash checksum, CancellationToken ct = default)
		{
			return ValueTask.FromResult<Stream?>(File.OpenRead(paths[beatmapId]));
		}
	}
}
