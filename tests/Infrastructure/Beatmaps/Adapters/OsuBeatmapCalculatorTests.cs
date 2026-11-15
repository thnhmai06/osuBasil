using Basil.Application.Beatmaps.Storage;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Infrastructure.Beatmaps.Adapters;
using Basil.Infrastructure.Beatmaps.Storage;
using Basil.Infrastructure.Beatmaps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Infrastructure.Beatmaps.Tests.Adapters;

/// <summary>
///     Pins the properties of the calculated numbers rather than the numbers themselves, and the way the calculator keeps
///     its attributes as files of the beatmapset.
/// </summary>
public sealed class OsuBeatmapCalculatorTests : IDisposable
{
	private readonly CalculatorWorld _world = new();
	private readonly OsuBeatmapCalculator _calculator;

	public OsuBeatmapCalculatorTests()
	{
		_calculator = NewCalculator();
	}

	public void Dispose()
	{
		_world.Dispose();
	}

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private OsuBeatmapCalculator NewCalculator()
	{
		return new OsuBeatmapCalculator(_world.Storage, NullLogger<OsuBeatmapCalculator>.Instance);
	}

	private static HitCounts Perfect(BeatmapRecord map)
	{
		return map.Metadata.ObjectCount switch
		{
			OsuObjectCount osu => new HitCounts(osu.Total, 0, 0, 0, 0, 0),
			TaikoObjectCount taiko => new HitCounts(taiko.Hits, 0, 0, 0, 0, 0),
			CatchObjectCount ctb => new HitCounts(ctb.Fruits, ctb.Droplets, ctb.TinyDroplets, 0, 0, 0),
			_ => new HitCounts(0, 0, 0, map.Metadata.ObjectCount.Total, 0, 0)
		};
	}

	/// <summary>The star rating of every mode is a usable number, and is kept as a file named by mode and mask.</summary>
	/// <param name="mode">The mode.</param>
	[Theory]
	[InlineData(GameMode.Standard)]
	[InlineData(GameMode.Taiko)]
	[InlineData(GameMode.Catch)]
	[InlineData(GameMode.Mania)]
	public async Task Stars_AreUsableAndKeptInTheSetFolder(GameMode mode)
	{
		var stars = await _calculator.GetStarsAsync(_world.Beatmaps[mode], mode, GameMods.NoMod, Ct);

		Assert.True(double.IsFinite(stars) && stars > 0);
		Assert.True(File.Exists(_world.AttributesFile(mode, GameMods.NoMod)));
		Assert.EndsWith(Path.Combine("attributes", _world.Beatmaps[mode].Hash.HashValue, ((int)mode).ToString(),
			"0.json"), _world.AttributesFile(mode, GameMods.NoMod));
	}

	/// <summary>A speed-up has more stars than no mod, and has a file of its own.</summary>
	[Fact]
	public async Task DoubleTime_HasMoreStarsThanNoMod()
	{
		var map = _world.Beatmaps[GameMode.Standard];

		var normal = await _calculator.GetStarsAsync(map, GameMode.Standard, GameMods.NoMod, Ct);
		var fast = await _calculator.GetStarsAsync(map, GameMode.Standard, GameMods.DoubleTime, Ct);

		Assert.True(fast > normal);
		Assert.True(File.Exists(_world.AttributesFile(GameMode.Standard, GameMods.DoubleTime)));
	}

	/// <summary>Plays that differ only in mods that do not change the difficulty share the file and the stars.</summary>
	[Fact]
	public async Task ScoringOnlyMods_ShareTheNoModFile()
	{
		var map = _world.Beatmaps[GameMode.Standard];
		var normal = await _calculator.GetStarsAsync(map, GameMode.Standard, GameMods.NoMod, Ct);

		var noisy = await _calculator.GetStarsAsync(map, GameMode.Standard,
			GameMods.NoFail | GameMods.SpunOut | GameMods.SuddenDeath, Ct);

		Assert.Equal(normal, noisy);
		Assert.Single(Directory.EnumerateFiles(Path.Combine(_world.Folder, FileBeatmapsetNames.AttributesFolder), "*",
			SearchOption.AllDirectories));
	}

	/// <summary>A later calculator reads the file and needs neither the beatmap file nor the calculation.</summary>
	[Fact]
	public async Task StoredAttributes_AnswerWithoutTheBeatmapFile()
	{
		var map = _world.Beatmaps[GameMode.Taiko];
		var first = await _calculator.GetStarsAsync(map, GameMode.Taiko, GameMods.NoMod, Ct);
		File.Delete(_world.OsuFile(map.Hash));

		var again = await NewCalculator().GetStarsAsync(map, GameMode.Taiko, GameMods.NoMod, Ct);

		Assert.Equal(first, again);
	}

	/// <summary>A file of another calculator version, or a damaged file, is calculated again and replaced.</summary>
	/// <param name="content">What the file holds.</param>
	[Theory]
	[InlineData("{\"Version\":-1,\"Attributes\":{\"9\":999}}")]
	[InlineData("this is not json")]
	public async Task StaleOrDamagedFile_IsCalculatedAgain(string content)
	{
		var map = _world.Beatmaps[GameMode.Catch];
		var expected = await _calculator.GetStarsAsync(map, GameMode.Catch, GameMods.NoMod, Ct);
		var path = _world.AttributesFile(GameMode.Catch, GameMods.NoMod);
		await File.WriteAllTextAsync(path, content, Ct);

		var stars = await NewCalculator().GetStarsAsync(map, GameMode.Catch, GameMods.NoMod, Ct);

		Assert.Equal(expected, stars);
		Assert.NotEqual(content, await File.ReadAllTextAsync(path, Ct));
	}

	/// <summary>A beatmap the server has no file for cannot be calculated, and the failure is not kept.</summary>
	[Fact]
	public async Task MissingBeatmapFile_Fails_AndIsNotKept()
	{
		var map = _world.Beatmaps[GameMode.Mania];
		var file = _world.OsuFile(map.Hash);
		var content = await File.ReadAllBytesAsync(file, Ct);
		File.Delete(file);

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			_calculator.GetStarsAsync(map, GameMode.Mania, GameMods.NoMod, Ct));

		await File.WriteAllBytesAsync(file, content, Ct);
		Assert.True(await _calculator.GetStarsAsync(map, GameMode.Mania, GameMods.NoMod, Ct) > 0);
	}

	/// <summary>Many calls for one combination at the same time all get the same stars and write one file.</summary>
	[Fact]
	public async Task ManyCallsForOneCombination_AllGetTheSameStars()
	{
		var map = _world.Beatmaps[GameMode.Standard];

		var all = await Task.WhenAll(Enumerable.Range(0, 16)
			.Select(_ => _calculator.GetStarsAsync(map, GameMode.Standard, GameMods.HardRock, Ct)));

		Assert.Single(all.Distinct());
		Assert.True(File.Exists(_world.AttributesFile(GameMode.Standard, GameMods.HardRock)));
	}

	/// <summary>A set deleted while its attributes are being written does not get its folder back.</summary>
	[Fact]
	public async Task WriteAfterTheSetIsDeleted_DoesNotCreateTheFolder()
	{
		var map = _world.Beatmaps[GameMode.Standard];
		await _world.Storage.DeleteAsync(_world.Set);

		var written = await _world.Storage.WriteAttributesAsync(_world.Set, map.Hash, GameMode.Standard,
			GameMods.NoMod, "{}");

		Assert.False(written);
		Assert.False(Directory.Exists(_world.Folder));
	}

	/// <summary>Clearing a set's derived files removes its attributes too, and keeps the archive.</summary>
	[Fact]
	public async Task ClearingTheSet_RemovesTheAttributes()
	{
		await _calculator.GetStarsAsync(_world.Beatmaps[GameMode.Standard], GameMode.Standard, GameMods.NoMod, Ct);

		await _world.Storage.ClearAsync(_world.Set);

		Assert.False(Directory.Exists(Path.Combine(_world.Folder, FileBeatmapsetNames.AttributesFolder)));
		Assert.True(File.Exists(Path.Combine(_world.Folder, FileBeatmapsetNames.ArchiveName)));
	}

	/// <summary>The pp of a perfect play is finite and positive, and no play is above it.</summary>
	/// <param name="mode">The mode.</param>
	[Theory]
	[InlineData(GameMode.Standard)]
	[InlineData(GameMode.Taiko)]
	[InlineData(GameMode.Catch)]
	[InlineData(GameMode.Mania)]
	public async Task PerfectPlay_HasPositivePpEqualToMaxPp(GameMode mode)
	{
		var map = _world.Beatmaps[mode];

		var result = await _calculator.GetPerformanceAsync(map, mode, GameMods.NoMod, Perfect(map),
			map.Metadata.ObjectCount.MaxCombo, Ct);

		Assert.True(double.IsFinite(result.Pp) && result.Pp > 0);
		Assert.True(result.MaxPp >= result.Pp);
	}

	/// <summary>More misses never give more pp.</summary>
	[Theory]
	[InlineData(GameMode.Standard)]
	[InlineData(GameMode.Taiko)]
	[InlineData(GameMode.Catch)]
	[InlineData(GameMode.Mania)]
	public async Task MoreMisses_NeverGiveMorePp(GameMode mode)
	{
		var map = _world.Beatmaps[mode];
		var perfect = Perfect(map);
		var combo = map.Metadata.ObjectCount.MaxCombo;
		var previous = double.PositiveInfinity;

		foreach (var misses in new[] { 0, 5, 20 })
		{
			var hits = mode == GameMode.Mania
				? perfect with { NumGeki = perfect.NumGeki - misses, NumMiss = misses }
				: perfect with { Num300 = perfect.Num300 - misses, NumMiss = misses };
			var pp = (await _calculator.GetPerformanceAsync(map, mode, GameMods.NoMod, hits, combo - misses * 2, Ct)).Pp;

			Assert.True(double.IsFinite(pp) && pp >= 0);
			Assert.True(pp <= previous, $"{misses} misses gave {pp} after {previous}");
			previous = pp;
		}
	}

	/// <summary>A speed-up raises the pp of a perfect play of osu!standard.</summary>
	[Fact]
	public async Task DoubleTime_RaisesThePpOfAPerfectPlay()
	{
		var map = _world.Beatmaps[GameMode.Standard];
		var combo = map.Metadata.ObjectCount.MaxCombo;

		var normal = await _calculator.GetPerformanceAsync(map, GameMode.Standard, GameMods.NoMod, Perfect(map), combo, Ct);
		var fast = await _calculator.GetPerformanceAsync(map, GameMode.Standard, GameMods.DoubleTime, Perfect(map), combo, Ct);

		Assert.True(fast.Pp > normal.Pp);
	}
}
