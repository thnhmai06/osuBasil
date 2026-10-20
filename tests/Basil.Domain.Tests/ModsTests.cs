using Basil.Domain.Mechanics;

namespace Basil.Domain.Tests;

/// <summary>Verifies gameplay-mod conflict resolution and text parsing.</summary>
public class ModsTests
{
	[Theory]
	[InlineData(GameMods.DoubleTime | GameMods.Nightcore, GameMods.Nightcore)]
	[InlineData(GameMods.DoubleTime | GameMods.HalfTime, GameMods.DoubleTime)]
	[InlineData(GameMods.Nightcore | GameMods.HalfTime, GameMods.Nightcore)]
	[InlineData(GameMods.Easy | GameMods.HardRock, GameMods.Easy)]
	[InlineData(GameMods.NoFail | GameMods.SuddenDeath, GameMods.NoFail)]
	[InlineData(GameMods.Relax | GameMods.SuddenDeath, GameMods.Relax)]
	[InlineData(GameMods.NoFail | GameMods.Perfect, GameMods.NoFail)]
	[InlineData(GameMods.Relax | GameMods.NoFail, GameMods.Relax)]
	[InlineData(GameMods.Autopilot | GameMods.NoFail, GameMods.Autopilot)]
	[InlineData(GameMods.Perfect | GameMods.SuddenDeath, GameMods.Perfect)]
	public void RemoveInvalidMods_ModeInspecificConflicts_ResolvesToExpected(GameMods input, GameMods expected)
	{
		Assert.Equal(expected, input.RemoveInvalidMods(GameMode.Standard));
	}

	[Fact]
	public void RemoveInvalidMods_NonOsuMode_RemovesOsuSpecificMods()
	{
		var result = (GameMods.Autopilot | GameMods.SpunOut | GameMods.Target | GameMods.Hidden)
			.RemoveInvalidMods(GameMode.Taiko);

		Assert.Equal(GameMods.Hidden, result);
	}

	[Fact]
	public void RemoveInvalidMods_NonManiaMode_RemovesManiaSpecificMods()
	{
		var result = (GameMods.Mirror | GameMods.Random | GameMods.FadeIn | GameMods.Key4 | GameMods.Hidden)
			.RemoveInvalidMods(GameMode.Standard);

		Assert.Equal(GameMods.Hidden, result);
	}

	[Fact]
	public void RemoveInvalidMods_StandardAutopilotWithSpunOut_RemovesAutopilot()
	{
		Assert.Equal(GameMods.SpunOut, (GameMods.Autopilot | GameMods.SpunOut).RemoveInvalidMods(GameMode.Standard));
	}

	[Fact]
	public void RemoveInvalidMods_StandardAutopilotWithRelax_RemovesAutopilot()
	{
		Assert.Equal(GameMods.Relax, (GameMods.Autopilot | GameMods.Relax).RemoveInvalidMods(GameMode.Standard));
	}

	[Fact]
	public void RemoveInvalidMods_Mania_RemovesRelax()
	{
		Assert.Equal(GameMods.NoMod, GameMods.Relax.RemoveInvalidMods(GameMode.Mania));
	}

	[Fact]
	public void RemoveInvalidMods_ManiaHiddenWithFadeIn_RemovesFadeIn()
	{
		Assert.Equal(GameMods.Hidden,
			(GameMods.Hidden | GameMods.FadeIn).RemoveInvalidMods(GameMode.Mania));
	}

	[Fact]
	public void RemoveInvalidMods_MultipleKeymods_KeepsOnlyFirst()
	{
		Assert.Equal(GameMods.Key1, (GameMods.Key1 | GameMods.Key2 | GameMods.Key4).RemoveInvalidMods(GameMode.Mania));
	}

	[Theory]
	[InlineData("HDDTRX", GameMods.Hidden | GameMods.DoubleTime | GameMods.Relax)]
	[InlineData("", GameMods.NoMod)]
	[InlineData("hddt", GameMods.Hidden | GameMods.DoubleTime)]
	[InlineData("ZZHD", GameMods.Hidden)]
	public void FromModString_ParsesTwoCharacterChunks(string input, GameMods expected)
	{
		Assert.Equal(expected, ModsExtensions.FromModString(input));
	}

	[Fact]
	public void FromNowPlayingString_ParsesTaggedTokensAndFiltersInvalidCombos()
	{
		Assert.Equal(GameMods.Hidden | GameMods.DoubleTime,
			ModsExtensions.FromNowPlayingString("+Hidden +DoubleTime", GameMode.Standard));
	}

	[Fact]
	public void FromNowPlayingString_UnknownToken_Ignored()
	{
		Assert.Equal(GameMods.Hidden,
			ModsExtensions.FromNowPlayingString("+Hidden +NotAMod", GameMode.Standard));
	}
}