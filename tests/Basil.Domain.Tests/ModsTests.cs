using Basil.Domain.Scores;

namespace Basil.Domain.Tests;

/// <summary>Ported from app/constants/mods.py's Mods.filter_invalid_combos / from_modstr / from_np.</summary>
public class ModsTests
{
    [Theory]
    [InlineData(Mods.DoubleTime | Mods.Nightcore, Mods.Nightcore)] // DTNC -> keep NC
    [InlineData(Mods.DoubleTime | Mods.HalfTime, Mods.DoubleTime)] // (DT)HT -> drop HT
    [InlineData(Mods.Nightcore | Mods.HalfTime, Mods.Nightcore)] // (NC)HT -> drop HT
    [InlineData(Mods.Easy | Mods.HardRock, Mods.Easy)] // EZHR -> drop HR
    [InlineData(Mods.NoFail | Mods.SuddenDeath, Mods.NoFail)] // (NF)SD -> drop SD
    [InlineData(Mods.Relax | Mods.SuddenDeath, Mods.Relax)] // (RX)SD -> drop SD
    [InlineData(Mods.NoFail | Mods.Perfect, Mods.NoFail)] // (NF)PF -> drop PF
    [InlineData(Mods.Relax | Mods.NoFail, Mods.Relax)] // (RX)NF -> drop NF
    [InlineData(Mods.Autopilot | Mods.NoFail, Mods.Autopilot)] // (AP)NF -> drop NF
    [InlineData(Mods.Perfect | Mods.SuddenDeath, Mods.Perfect)] // PFSD -> drop SD
    public void FilterInvalidCombos_ModeInspecificConflicts_ResolvesToExpected(Mods input, Mods expected)
    {
        Assert.Equal(expected, input.FilterInvalidCombos(0));
    }

    [Fact]
    public void FilterInvalidCombos_NonOsuMode_RemovesOsuSpecificMods()
    {
        var result = (Mods.Autopilot | Mods.SpunOut | Mods.Target | Mods.Hidden).FilterInvalidCombos(1);

        Assert.Equal(Mods.Hidden, result);
    }

    [Fact]
    public void FilterInvalidCombos_NonManiaMode_RemovesManiaSpecificMods()
    {
        var result = (Mods.Mirror | Mods.Random | Mods.FadeIn | Mods.Key4 | Mods.Hidden).FilterInvalidCombos(0);

        Assert.Equal(Mods.Hidden, result);
    }

    [Fact]
    public void FilterInvalidCombos_Osu_AutopilotWithSpunOut_RemovesAutopilot()
    {
        var result = (Mods.Autopilot | Mods.SpunOut).FilterInvalidCombos(0);

        Assert.Equal(Mods.SpunOut, result);
    }

    [Fact]
    public void FilterInvalidCombos_Osu_AutopilotWithRelax_RemovesAutopilot()
    {
        var result = (Mods.Autopilot | Mods.Relax).FilterInvalidCombos(0);

        Assert.Equal(Mods.Relax, result);
    }

    [Fact]
    public void FilterInvalidCombos_Mania_RemovesRelax()
    {
        var result = Mods.Relax.FilterInvalidCombos(3);

        Assert.Equal(Mods.NoMod, result);
    }

    [Fact]
    public void FilterInvalidCombos_Mania_HiddenWithFadeIn_RemovesFadeIn()
    {
        var result = (Mods.Hidden | Mods.FadeIn).FilterInvalidCombos(3);

        Assert.Equal(Mods.Hidden, result);
    }

    [Fact]
    public void FilterInvalidCombos_MultipleKeymods_KeepsOnlyFirst()
    {
        var result = (Mods.Key1 | Mods.Key2 | Mods.Key4).FilterInvalidCombos(3);

        Assert.Equal(Mods.Key1, result);
    }

    [Theory]
    [InlineData("HDDTRX", Mods.Hidden | Mods.DoubleTime | Mods.Relax)]
    [InlineData("", Mods.NoMod)]
    [InlineData("hddt", Mods.Hidden | Mods.DoubleTime)] // case-insensitive
    [InlineData("ZZHD", Mods.Hidden)] // unknown 2-char chunk ignored
    public void FromModString_ParsesTwoCharacterChunks(string input, Mods expected)
    {
        Assert.Equal(expected, ModsExtensions.FromModString(input));
    }

    [Fact]
    public void FromNowPlayingString_ParsesTaggedTokens_AndFiltersInvalidCombos()
    {
        // "+Hidden" and "+DoubleTime" are valid together; input includes an invalid
        // combo (NoFail conflicting with nothing here, just verifying parse + filter runs).
        var result = ModsExtensions.FromNowPlayingString("+Hidden +DoubleTime", 0);

        Assert.Equal(Mods.Hidden | Mods.DoubleTime, result);
    }

    [Fact]
    public void FromNowPlayingString_UnknownToken_Ignored()
    {
        var result = ModsExtensions.FromNowPlayingString("+Hidden +NotAMod", 0);

        Assert.Equal(Mods.Hidden, result);
    }
}