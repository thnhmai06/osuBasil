using System.IO.Compression;
using Basil.Domain.Game.Mechanics;
using Basil.Infrastructure.Beatmaps.Adapters;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Beatmaps.Tests.Adapters;

public sealed class OsuBeatmapAnalyserTests
{
	private readonly OsuBeatmapAnalyser _analyser = new();

	private static MemoryStream Open(string name)
	{
		return new MemoryStream(OszArchive.Sample(Path.Combine("Beatmaps", name)), false);
	}

	[Theory]
	[InlineData("standard.osu", GameMode.Standard)]
	[InlineData("taiko.osu", GameMode.Taiko)]
	[InlineData("catch.osu", GameMode.Catch)]
	[InlineData("mania.osu", GameMode.Mania)]
	public void Analyze_GivesUsableStatsForEveryMode(string file, GameMode mode)
	{
		using var content = Open(file);

		var analysis = _analyser.Analyze(content, mode, GameMods.NoMod);

		Assert.Equal(mode, analysis.Difficulty.Mode);
		Assert.True(double.IsFinite(analysis.Difficulty.Star));
		Assert.True(analysis.Difficulty.Star > 0);
		Assert.True(analysis.Difficulty.Bpm > 0);
		Assert.True(analysis.Difficulty.Length > TimeSpan.Zero);
		Assert.True(analysis.ObjectCount.Total > 0);
		Assert.True(analysis.ObjectCount.MaxCombo > 0);
		Assert.Equal(ExpectedType(mode), analysis.ObjectCount.GetType());
	}

	[Fact]
	public void Analyze_EveryDifficultyOfARealSetIsUsableAndDoubleTimeNeverLowersIt()
	{
		using var archive = new ZipArchive(
			new MemoryStream(OszArchive.Sample(Path.Combine("Sets", "padoru-standard.osz")), false));
		var files = archive.Entries.Where(entry => entry.Name.EndsWith(".osu", StringComparison.Ordinal)).ToList();
		Assert.Equal(26, files.Count);

		foreach (var entry in files)
		{
			var normal = _analyser.Analyze(Read(entry), GameMode.Standard, GameMods.NoMod);
			var fast = _analyser.Analyze(Read(entry), GameMode.Standard, GameMods.DoubleTime);

			Assert.True(double.IsFinite(normal.Difficulty.Star) && normal.Difficulty.Star > 0, entry.Name);
			Assert.True(fast.Difficulty.Star >= normal.Difficulty.Star, entry.Name);
			Assert.True(fast.Difficulty.Length < normal.Difficulty.Length, entry.Name);
			Assert.True(normal.ObjectCount.Total > 0, entry.Name);
		}
	}

	private static MemoryStream Read(ZipArchiveEntry entry)
	{
		using var stream = entry.Open();
		var buffer = new MemoryStream();
		stream.CopyTo(buffer);
		buffer.Position = 0;
		return buffer;
	}

	[Fact]
	public void Analyze_WithDoubleTimeRaisesTheStarsAndShortensTheLength()
	{
		using var normalContent = Open("standard.osu");
		using var fastContent = Open("standard.osu");

		var normal = _analyser.Analyze(normalContent, GameMode.Standard, GameMods.NoMod);
		var fast = _analyser.Analyze(fastContent, GameMode.Standard, GameMods.DoubleTime);

		Assert.True(fast.Difficulty.Star > normal.Difficulty.Star);
		Assert.True(fast.Difficulty.Length < normal.Difficulty.Length);
		Assert.True(fast.Difficulty.Bpm > normal.Difficulty.Bpm);
	}

	[Fact]
	public void Analyze_WithHalfTimeLowersTheStars()
	{
		using var normalContent = Open("standard.osu");
		using var slowContent = Open("standard.osu");

		var normal = _analyser.Analyze(normalContent, GameMode.Standard, GameMods.NoMod);
		var slow = _analyser.Analyze(slowContent, GameMode.Standard, GameMods.HalfTime);

		Assert.True(slow.Difficulty.Star < normal.Difficulty.Star);
	}

	[Fact]
	public void Analyze_WithHardRockRaisesCircleSizeAndApproachRate()
	{
		using var normalContent = Open("standard.osu");
		using var hardRockContent = Open("standard.osu");

		var normal = _analyser.Analyze(normalContent, GameMode.Standard, GameMods.NoMod);
		var hardRock = _analyser.Analyze(hardRockContent, GameMode.Standard, GameMods.HardRock);

		Assert.True(hardRock.Difficulty.Cs > normal.Difficulty.Cs);
		Assert.True(hardRock.Difficulty.Ar > normal.Difficulty.Ar);
	}

	[Fact]
	public void Analyze_WithEasyLowersCircleSizeAndApproachRate()
	{
		using var normalContent = Open("standard.osu");
		using var easyContent = Open("standard.osu");

		var normal = _analyser.Analyze(normalContent, GameMode.Standard, GameMods.NoMod);
		var easy = _analyser.Analyze(easyContent, GameMode.Standard, GameMods.Easy);

		Assert.True(easy.Difficulty.Cs < normal.Difficulty.Cs);
		Assert.True(easy.Difficulty.Ar < normal.Difficulty.Ar);
	}

	[Fact]
	public void Analyze_TwiceGivesTheSameNumbers()
	{
		using var firstContent = Open("standard.osu");
		using var secondContent = Open("standard.osu");

		var first = _analyser.Analyze(firstContent, GameMode.Standard, GameMods.Hidden | GameMods.DoubleTime);
		var second = _analyser.Analyze(secondContent, GameMode.Standard, GameMods.Hidden | GameMods.DoubleTime);

		Assert.Equal(first.Difficulty, second.Difficulty);
		Assert.Equal(first.ObjectCount.Total, second.ObjectCount.Total);
		Assert.Equal(first.ObjectCount.MaxCombo, second.ObjectCount.MaxCombo);
	}

	[Fact]
	public void Analyze_CountsEveryStandardObject()
	{
		using var content = Open("standard.osu");

		var analysis = _analyser.Analyze(content, GameMode.Standard, GameMods.NoMod);

		var objects = Assert.IsType<OsuObjectCount>(analysis.ObjectCount);
		Assert.Equal(objects.Circles + objects.Sliders + objects.Spinners, objects.Total);
		Assert.True(objects.Circles > 0);
		Assert.True(objects.Sliders > 0);
	}

	[Fact]
	public void Analyze_CountsTheNestedPartsOfACatchDifficulty()
	{
		using var content = Open("catch.osu");

		var analysis = _analyser.Analyze(content, GameMode.Catch, GameMods.NoMod);

		var objects = Assert.IsType<CatchObjectCount>(analysis.ObjectCount);
		Assert.Equal(objects.Fruits + objects.Droplets + objects.TinyDroplets + objects.Bananas, objects.Total);
		Assert.True(objects.Droplets + objects.TinyDroplets > 0);
	}

	[Fact]
	public void Analyze_CountsNotesAndHoldsOfAManiaDifficulty()
	{
		using var content = Open("mania.osu");

		var analysis = _analyser.Analyze(content, GameMode.Mania, GameMods.NoMod);

		var objects = Assert.IsType<ManiaObjectCount>(analysis.ObjectCount);
		Assert.Equal(objects.Notes + objects.HoldNotes, objects.Total);
		Assert.True(objects.Notes > 0);
	}

	private static Type ExpectedType(GameMode mode)
	{
		return mode switch
		{
			GameMode.Standard => typeof(OsuObjectCount),
			GameMode.Taiko => typeof(TaikoObjectCount),
			GameMode.Catch => typeof(CatchObjectCount),
			GameMode.Mania => typeof(ManiaObjectCount),
			_ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
		};
	}
}