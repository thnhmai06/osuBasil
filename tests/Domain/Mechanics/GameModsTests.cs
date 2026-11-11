using Basil.Domain.Mechanics;

namespace Basil.Domain.Tests.Mechanics;

/// <summary>Verifies gameplay-mod conflict resolution and text parsing.</summary>
public class GameModsTests
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
	public void ConflictsAnyMode_ResolvedAsExpected(GameMods input, GameMods expected)
	{
		Assert.Equal(expected, input.RemoveInvalidMods(GameMode.Standard));
	}

	[Fact]
	public void NonOsuMode_OsuModsRemoved()
	{
		var result = (GameMods.Autopilot | GameMods.SpunOut | GameMods.Target | GameMods.Hidden)
			.RemoveInvalidMods(GameMode.Taiko);

		Assert.Equal(GameMods.Hidden, result);
	}

	[Fact]
	public void NonManiaMode_ManiaModsRemoved()
	{
		var result = (GameMods.Mirror | GameMods.Random | GameMods.FadeIn | GameMods.Key4 | GameMods.Hidden)
			.RemoveInvalidMods(GameMode.Standard);

		Assert.Equal(GameMods.Hidden, result);
	}

	[Theory]
	[InlineData(GameMode.Standard)]
	[InlineData(GameMode.Taiko)]
	[InlineData(GameMode.Catch)]
	public void KeyCoopOutside_Removed(GameMode mode)
	{
		Assert.Equal(GameMods.Hidden, (GameMods.KeyCoop | GameMods.Hidden).RemoveInvalidMods(mode));
	}

	[Fact]
	public void KeyCoopMania_Kept()
	{
		Assert.Equal(GameMods.Key5 | GameMods.KeyCoop,
			(GameMods.Key5 | GameMods.KeyCoop).RemoveInvalidMods(GameMode.Mania));
	}

	[Theory]
	[InlineData(GameMode.Standard)]
	[InlineData(GameMode.Taiko)]
	[InlineData(GameMode.Catch)]
	public void KeyCoopOutside_Rejected(GameMode mode)
	{
		Assert.False(GameMods.KeyCoop.IsValid(mode));
	}

	[Fact]
	public void KeyCoopMania_Accepted()
	{
		Assert.True(GameMods.KeyCoop.IsValid(GameMode.Mania));
	}

	[Fact]
	public void AutopilotSpunOut_AutopilotRemoved()
	{
		Assert.Equal(GameMods.SpunOut, (GameMods.Autopilot | GameMods.SpunOut).RemoveInvalidMods(GameMode.Standard));
	}

	[Fact]
	public void AutopilotRelax_AutopilotRemoved()
	{
		Assert.Equal(GameMods.Relax, (GameMods.Autopilot | GameMods.Relax).RemoveInvalidMods(GameMode.Standard));
	}

	[Fact]
	public void ManiaRelax_RelaxRemoved()
	{
		Assert.Equal(GameMods.NoMod, GameMods.Relax.RemoveInvalidMods(GameMode.Mania));
	}

	[Fact]
	public void ManiaHidden_FadeInRemoved()
	{
		Assert.Equal(GameMods.Hidden,
			(GameMods.Hidden | GameMods.FadeIn).RemoveInvalidMods(GameMode.Mania));
	}

	[Fact]
	public void MultipleKeymods_FirstKept()
	{
		Assert.Equal(GameMods.Key1, (GameMods.Key1 | GameMods.Key2 | GameMods.Key4).RemoveInvalidMods(GameMode.Mania));
	}

	[Theory]
	[InlineData("HDDTRX", GameMods.Hidden | GameMods.DoubleTime | GameMods.Relax)]
	[InlineData("", GameMods.NoMod)]
	[InlineData("hddt", GameMods.Hidden | GameMods.DoubleTime)]
	[InlineData("ZZHD", GameMods.Hidden)]
	public void ModString_ParsesChunks(string input, GameMods expected)
	{
		Assert.Equal(expected, ModsExtensions.FromModString(input));
	}

	[Fact]
	public void NowPlaying_ParsesAndFilters()
	{
		Assert.Equal(GameMods.Hidden | GameMods.DoubleTime,
			ModsExtensions.FromNowPlayingString("+Hidden +DoubleTime", GameMode.Standard));
	}

	[Fact]
	public void NowPlayingUnknown_Ignored()
	{
		Assert.Equal(GameMods.Hidden,
			ModsExtensions.FromNowPlayingString("+Hidden +NotAMod", GameMode.Standard));
	}

	[Fact]
	public void NightcoreWire_DoubleTime()
	{
		var wireForm = GameMods.DoubleTime | GameMods.Nightcore;

		Assert.Equal(576, (int)wireForm);
	}

	[Fact]
	public void PerfectWire_SuddenDeath()
	{
		var wireForm = GameMods.Perfect | GameMods.SuddenDeath;

		Assert.Equal(16416, (int)wireForm);
	}

	[Theory]
	[InlineData(GameMods.Nightcore)]
	[InlineData(GameMods.Perfect)]
	[InlineData(GameMods.DoubleTime | GameMods.Nightcore)]
	[InlineData(GameMods.Perfect | GameMods.SuddenDeath)]
	public void ValidSingleWire_Accepted(GameMods mods)
	{
		Assert.True(mods.IsValid(GameMode.Standard));
	}

	[Theory]
	[InlineData(GameMods.DoubleTime | GameMods.HalfTime)]
	[InlineData(GameMods.Nightcore | GameMods.HalfTime)]
	[InlineData(GameMods.DoubleTime | GameMods.Nightcore | GameMods.HalfTime)]
	[InlineData(GameMods.Perfect | GameMods.NoFail)]
	public void ConflictingMods_Rejected(GameMods mods)
	{
		Assert.False(mods.IsValid(GameMode.Standard));
	}
}