using Basil.Domain.Beatmaps;

namespace Basil.Domain.Tests;

/// <summary>Ported from app/constants/gamemodes.py's GameMode.from_params / as_vanilla / valid_gamemodes.</summary>
public class GameModeTests
{
    [Theory]
    [InlineData(0, Mods.NoMod, GameMode.VanillaOsu)]
    [InlineData(1, Mods.NoMod, GameMode.VanillaTaiko)]
    [InlineData(2, Mods.NoMod, GameMode.VanillaCatch)]
    [InlineData(3, Mods.NoMod, GameMode.VanillaMania)]
    [InlineData(0, Mods.Relax, GameMode.RelaxOsu)]
    [InlineData(1, Mods.Relax, GameMode.RelaxTaiko)]
    [InlineData(2, Mods.Relax, GameMode.RelaxCatch)]
    [InlineData(0, Mods.Autopilot, GameMode.AutopilotOsu)]
    public void FromParams_CombinesVanillaModeAndRelaxAutopilotMods(int modeVn, Mods mods, GameMode expected)
    {
        Assert.Equal(expected, GameModeExtensions.FromParams(modeVn, mods));
    }

    [Fact]
    public void FromParams_AutopilotTakesPrecedenceOverRelax()
    {
        // matches app/constants/gamemodes.py: `if mods & AUTOPILOT: +8 elif mods & RELAX: +4`
        Assert.Equal(GameMode.AutopilotOsu, GameModeExtensions.FromParams(0, Mods.Relax | Mods.Autopilot));
    }

    [Theory]
    [InlineData(GameMode.VanillaOsu, 0)]
    [InlineData(GameMode.VanillaMania, 3)]
    [InlineData(GameMode.RelaxOsu, 0)]
    [InlineData(GameMode.RelaxCatch, 2)]
    [InlineData(GameMode.AutopilotOsu, 0)]
    public void AsVanilla_ReturnsModValueMod4(GameMode mode, int expectedVanilla)
    {
        Assert.Equal(expectedVanilla, mode.AsVanilla());
    }

    [Fact]
    public void ValidGameModes_ExcludesUnusedRelaxManiaAndAutopilotNonOsuModes()
    {
        var valid = GameModeExtensions.ValidGameModes();

        Assert.DoesNotContain(GameMode.RelaxMania, valid);
        Assert.DoesNotContain(GameMode.AutopilotTaiko, valid);
        Assert.DoesNotContain(GameMode.AutopilotCatch, valid);
        Assert.DoesNotContain(GameMode.AutopilotMania, valid);
        Assert.Contains(GameMode.VanillaOsu, valid);
        Assert.Contains(GameMode.RelaxOsu, valid);
        Assert.Contains(GameMode.AutopilotOsu, valid);
        Assert.Equal(8, valid.Count);
    }
}