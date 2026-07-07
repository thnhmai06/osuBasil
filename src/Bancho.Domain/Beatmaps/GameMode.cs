namespace Bancho.Domain.Beatmaps;

/// <summary>Ported from app/constants/gamemodes.py's GameMode (IntEnum).</summary>
public enum GameMode
{
    VanillaOsu = 0,
    VanillaTaiko = 1,
    VanillaCatch = 2,
    VanillaMania = 3,

    RelaxOsu = 4,
    RelaxTaiko = 5,
    RelaxCatch = 6,
    RelaxMania = 7, // unused

    AutopilotOsu = 8,
    AutopilotTaiko = 9, // unused
    AutopilotCatch = 10, // unused
    AutopilotMania = 11, // unused
}

/// <summary>Ported from app/constants/gamemodes.py's GameMode methods (from_params, as_vanilla, valid_gamemodes).</summary>
public static class GameModeExtensions
{
    private static readonly GameMode[] UnusedModes =
    [
        GameMode.RelaxMania,
        GameMode.AutopilotTaiko,
        GameMode.AutopilotCatch,
        GameMode.AutopilotMania,
    ];

    /// <summary>Combines a vanilla mode (0-3) with Relax/Autopilot mods. Autopilot takes precedence over Relax.</summary>
    public static GameMode FromParams(int modeVn, Mods mods)
    {
        var mode = modeVn;

        if ((mods & Mods.Autopilot) != Mods.NoMod)
        {
            mode += 8;
        }
        else if ((mods & Mods.Relax) != Mods.NoMod)
        {
            mode += 4;
        }

        return (GameMode)mode;
    }

    /// <summary>Returns the vanilla (rx/ap-stripped) equivalent mode value, e.g. RelaxCatch -> 2 (VanillaCatch).</summary>
    public static int AsVanilla(this GameMode mode) => (int)mode % 4;

    /// <summary>All gamemodes actually reachable in practice (excludes unused rx!mania / ap!taiko-catch-mania).</summary>
    public static IReadOnlyList<GameMode> ValidGameModes() =>
        Enum.GetValues<GameMode>().Where(m => !UnusedModes.Contains(m)).ToArray();
}
