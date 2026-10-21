using Basil.Application.Persistence.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Infrastructure.Capabilities.Beatmaps;
using Basil.Infrastructure.Capabilities.Tests.Support;
using NSubstitute;

namespace Basil.Infrastructure.Capabilities.Tests.Beatmaps;

/// <summary>
///     Pins the properties of the analysed stats rather than their exact numbers: the analyser follows the osu!
///     rulesets, so the contract is that a valid difficulty gives usable, mod-consistent numbers.
/// </summary>
public sealed class OsuBeatmapAnalyserTests
{
	private readonly OsuBeatmapAnalyser _analyser = new(Substitute.For<IBeatmapsetStorage>());

	/// <summary>Reads a sample difficulty again, from its start, for one analysis.</summary>
	/// <param name="name">The sample's file name under the assets folder.</param>
	/// <returns>The difficulty's content.</returns>
	private static MemoryStream Open(string name)
	{
		return new MemoryStream(OszArchive.Sample(Path.Combine("Beatmaps", name)), false);
	}

	/// <summary>A difficulty of every mode gives a usable star rating and its own kind of object counts.</summary>
	/// <param name="file">The sample difficulty.</param>
	/// <param name="mode">The mode it is played in.</param>
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
		Assert.True(analysis.Objects.Total > 0);
		Assert.True(analysis.Objects.MaxCombo > 0);
		Assert.Equal(ExpectedType(mode), analysis.Objects.GetType());
	}

	/// <summary>A speed-up raises the star rating and shortens the playable length.</summary>
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

	/// <summary>A slow-down lowers the star rating.</summary>
	[Fact]
	public void Analyze_WithHalfTimeLowersTheStars()
	{
		using var normalContent = Open("standard.osu");
		using var slowContent = Open("standard.osu");

		var normal = _analyser.Analyze(normalContent, GameMode.Standard, GameMods.NoMod);
		var slow = _analyser.Analyze(slowContent, GameMode.Standard, GameMods.HalfTime);

		Assert.True(slow.Difficulty.Star < normal.Difficulty.Star);
	}

	/// <summary>Hard Rock raises circle size and approach rate.</summary>
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

	/// <summary>Easy lowers circle size and approach rate.</summary>
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

	/// <summary>The same difficulty under the same mods gives the same numbers every time.</summary>
	[Fact]
	public void Analyze_TwiceGivesTheSameNumbers()
	{
		using var firstContent = Open("standard.osu");
		using var secondContent = Open("standard.osu");

		var first = _analyser.Analyze(firstContent, GameMode.Standard, GameMods.Hidden | GameMods.DoubleTime);
		var second = _analyser.Analyze(secondContent, GameMode.Standard, GameMods.Hidden | GameMods.DoubleTime);

		Assert.Equal(first.Difficulty, second.Difficulty);
		Assert.Equal(first.Objects.Total, second.Objects.Total);
		Assert.Equal(first.Objects.MaxCombo, second.Objects.MaxCombo);
	}

	/// <summary>A standard difficulty is counted as circles, sliders and spinners.</summary>
	[Fact]
	public void Analyze_CountsEveryStandardObject()
	{
		using var content = Open("standard.osu");

		var analysis = _analyser.Analyze(content, GameMode.Standard, GameMods.NoMod);

		var objects = Assert.IsType<OsuObjects>(analysis.Objects);
		Assert.Equal(objects.Circles + objects.Sliders + objects.Spinners, objects.Total);
		Assert.True(objects.Circles > 0);
		Assert.True(objects.Sliders > 0);
	}

	/// <summary>A catch difficulty counts the parts of its juice streams, not just the streams.</summary>
	[Fact]
	public void Analyze_CountsTheNestedPartsOfACatchDifficulty()
	{
		using var content = Open("catch.osu");

		var analysis = _analyser.Analyze(content, GameMode.Catch, GameMods.NoMod);

		var objects = Assert.IsType<CatchObjects>(analysis.Objects);
		Assert.Equal(objects.Fruits + objects.Droplets + objects.TinyDroplets + objects.Bananas, objects.Total);
		Assert.True(objects.Droplets + objects.TinyDroplets > 0);
	}

	/// <summary>A mania difficulty counts notes and holds.</summary>
	[Fact]
	public void Analyze_CountsNotesAndHoldsOfAManiaDifficulty()
	{
		using var content = Open("mania.osu");

		var analysis = _analyser.Analyze(content, GameMode.Mania, GameMods.NoMod);

		var objects = Assert.IsType<ManiaObjects>(analysis.Objects);
		Assert.Equal(objects.Notes + objects.HoldNotes, objects.Total);
		Assert.True(objects.Notes > 0);
	}

	/// <summary>The object counts of a mode are the ones that mode names.</summary>
	/// <param name="mode">The game mode.</param>
	/// <returns>The object count type the mode uses.</returns>
	private static Type ExpectedType(GameMode mode)
	{
		return mode switch
		{
			GameMode.Standard => typeof(OsuObjects),
			GameMode.Taiko => typeof(TaikoObjects),
			GameMode.Catch => typeof(CatchObjects),
			GameMode.Mania => typeof(ManiaObjects),
			_ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
		};
	}
}
