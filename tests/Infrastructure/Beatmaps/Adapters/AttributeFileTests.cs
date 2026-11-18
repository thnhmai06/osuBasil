using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Infrastructure.Beatmaps.Adapters;
using Basil.Infrastructure.Beatmaps.Tests.Support;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Beatmaps;

namespace Basil.Infrastructure.Beatmaps.Tests.Adapters;

public sealed class AttributeFileTests : IDisposable
{
	private readonly CalculatorWorld _world = new();

	public void Dispose()
	{
		_world.Dispose();
	}

	private DifficultyAttributes Calculate(GameMode mode, GameMods mask, out int version)
	{
		var ruleset = OsuBeatmapAnalyser.CreateRuleset(mode);
		using var stream = File.OpenRead(_world.OsuFile(_world.Beatmaps[mode].Hash));
		var working = new StreamlessWorkingBeatmap(OsuBeatmapAnalyser.Decode(stream));
		var calculator = ruleset.CreateDifficultyCalculator(working);
		version = calculator.Version;
		return calculator.Calculate(ruleset.ConvertFromLegacyMods((LegacyMods)mask).ToArray());
	}

	private static DifficultyAttributes Empty(GameMode mode)
	{
		return mode switch
		{
			GameMode.Standard => new OsuDifficultyAttributes(),
			GameMode.Taiko => new osu.Game.Rulesets.Taiko.Difficulty.TaikoDifficultyAttributes(),
			GameMode.Catch => new osu.Game.Rulesets.Catch.Difficulty.CatchDifficultyAttributes(),
			_ => new osu.Game.Rulesets.Mania.Difficulty.ManiaDifficultyAttributes()
		};
	}

	[Theory]
	[InlineData(GameMode.Standard, GameMods.NoMod)]
	[InlineData(GameMode.Standard, GameMods.DoubleTime | GameMods.HardRock | GameMods.Flashlight)]
	[InlineData(GameMode.Taiko, GameMods.NoMod)]
	[InlineData(GameMode.Taiko, GameMods.DoubleTime)]
	[InlineData(GameMode.Catch, GameMods.NoMod)]
	[InlineData(GameMode.Catch, GameMods.HardRock)]
	[InlineData(GameMode.Mania, GameMods.NoMod)]
	[InlineData(GameMode.Mania, GameMods.HalfTime)]
	public void StoredAttributes_ReadBackWithTheSameNumbers(GameMode mode, GameMods mods)
	{
		var original = Calculate(mode, mods, out var version);

		var restored = Empty(mode);
		var read = AttributeFile.TryRead(AttributeFile.Write(version, original), version, restored,
			new StoredOnlineInfo(_world.Beatmaps[mode]));

		Assert.True(read);
		Assert.Equal(original.StarRating, restored.StarRating);
		Assert.Equal(original.MaxCombo, restored.MaxCombo);
		Assert.Equal(Pairs(original), Pairs(restored));
	}

	[Fact]
	public void StandardAttributes_TakeTheirObjectCountsFromTheStoredBeatmap()
	{
		var original = (OsuDifficultyAttributes)Calculate(GameMode.Standard, GameMods.NoMod, out var version);
		var counts = (OsuObjectCount)_world.Beatmaps[GameMode.Standard].Metadata.ObjectCount;

		var restored = new OsuDifficultyAttributes();
		Assert.True(AttributeFile.TryRead(AttributeFile.Write(version, original), version, restored,
			new StoredOnlineInfo(_world.Beatmaps[GameMode.Standard])));

		Assert.Equal(counts.Circles, restored.HitCircleCount);
		Assert.Equal(counts.Sliders, restored.SliderCount);
		Assert.Equal(counts.Spinners, restored.SpinnerCount);
		Assert.Equal(original.HitCircleCount, restored.HitCircleCount);
	}

	[Fact]
	public void OtherVersion_IsNotRead()
	{
		var original = Calculate(GameMode.Taiko, GameMods.NoMod, out var version);

		Assert.False(AttributeFile.TryRead(AttributeFile.Write(version, original), version + 1,
			Empty(GameMode.Taiko), new StoredOnlineInfo(_world.Beatmaps[GameMode.Taiko])));
	}

	[Theory]
	[InlineData("")]
	[InlineData("not json at all")]
	[InlineData("{")]
	[InlineData("null")]
	[InlineData("{\"Version\":0}")]
	[InlineData("{\"Version\":0,\"Attributes\":{\"x\":1}}")]
	[InlineData("{\"Version\":0,\"Attributes\":{\"1\":\"text\"}}")]
	public void DamagedText_IsNotRead(string text)
	{
		Assert.False(AttributeFile.TryRead(text, 0, Empty(GameMode.Standard),
			new StoredOnlineInfo(_world.Beatmaps[GameMode.Standard])));
	}

	private static List<(int Id, double Value)> Pairs(DifficultyAttributes attributes)
	{
		return [.. attributes.ToDatabaseAttributes().Select(pair => (pair.attributeId, Convert.ToDouble(pair.value)))
			.OrderBy(pair => pair.attributeId)];
	}
}
