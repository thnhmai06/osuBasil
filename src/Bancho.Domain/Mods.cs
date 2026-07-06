namespace Bancho.Domain;

/// <summary>Ported from app/constants/mods.py's Mods (IntFlag).</summary>
[Flags]
public enum Mods
{
    NoMod = 0,
    NoFail = 1 << 0,
    Easy = 1 << 1,
    TouchScreen = 1 << 2, // old: 'NoVideo'
    Hidden = 1 << 3,
    HardRock = 1 << 4,
    SuddenDeath = 1 << 5,
    DoubleTime = 1 << 6,
    Relax = 1 << 7,
    HalfTime = 1 << 8,
    Nightcore = 1 << 9,
    Flashlight = 1 << 10,
    Autoplay = 1 << 11,
    SpunOut = 1 << 12,
    Autopilot = 1 << 13,
    Perfect = 1 << 14,
    Key4 = 1 << 15,
    Key5 = 1 << 16,
    Key6 = 1 << 17,
    Key7 = 1 << 18,
    Key8 = 1 << 19,
    FadeIn = 1 << 20,
    Random = 1 << 21,
    Cinema = 1 << 22,
    Target = 1 << 23,
    Key9 = 1 << 24,
    KeyCoop = 1 << 25,
    Key1 = 1 << 26,
    Key3 = 1 << 27,
    Key2 = 1 << 28,
    ScoreV2 = 1 << 29,
    Mirror = 1 << 30,
}

/// <summary>
/// Ported from app/constants/mods.py's Mods methods (filter_invalid_combos, from_modstr, from_np)
/// — real mod-combination business rules, not just constant values.
/// </summary>
public static class ModsExtensions
{
    /// <summary>Ported from app/constants/mods.py's SPEED_CHANGING_MODS — used by multiplayer's freemods split between match-wide and per-slot mods.</summary>
    public const Mods SpeedChangingMods = Mods.DoubleTime | Mods.Nightcore | Mods.HalfTime;

    private const Mods KeyMods = Mods.Key1 | Mods.Key2 | Mods.Key3 | Mods.Key4 | Mods.Key5
        | Mods.Key6 | Mods.Key7 | Mods.Key8 | Mods.Key9;

    private const Mods OsuSpecificMods = Mods.Autopilot | Mods.SpunOut | Mods.Target;
    private const Mods ManiaSpecificMods = Mods.Mirror | Mods.Random | Mods.FadeIn | KeyMods;

    private static readonly Dictionary<string, Mods> ModStrToMod = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NF"] = Mods.NoFail,
        ["EZ"] = Mods.Easy,
        ["TD"] = Mods.TouchScreen,
        ["HD"] = Mods.Hidden,
        ["HR"] = Mods.HardRock,
        ["SD"] = Mods.SuddenDeath,
        ["DT"] = Mods.DoubleTime,
        ["RX"] = Mods.Relax,
        ["HT"] = Mods.HalfTime,
        ["NC"] = Mods.Nightcore,
        ["FL"] = Mods.Flashlight,
        ["AU"] = Mods.Autoplay,
        ["SO"] = Mods.SpunOut,
        ["AP"] = Mods.Autopilot,
        ["PF"] = Mods.Perfect,
        ["FI"] = Mods.FadeIn,
        ["RN"] = Mods.Random,
        ["CN"] = Mods.Cinema,
        ["TP"] = Mods.Target,
        ["V2"] = Mods.ScoreV2,
        ["MR"] = Mods.Mirror,
        ["1K"] = Mods.Key1,
        ["2K"] = Mods.Key2,
        ["3K"] = Mods.Key3,
        ["4K"] = Mods.Key4,
        ["5K"] = Mods.Key5,
        ["6K"] = Mods.Key6,
        ["7K"] = Mods.Key7,
        ["8K"] = Mods.Key8,
        ["9K"] = Mods.Key9,
        ["CO"] = Mods.KeyCoop,
    };

    private static readonly Dictionary<string, Mods> NpStrToMod = new()
    {
        ["-NoFail"] = Mods.NoFail,
        ["-Easy"] = Mods.Easy,
        ["+Hidden"] = Mods.Hidden,
        ["+HardRock"] = Mods.HardRock,
        ["+SuddenDeath"] = Mods.SuddenDeath,
        ["+DoubleTime"] = Mods.DoubleTime,
        ["~Relax~"] = Mods.Relax,
        ["-HalfTime"] = Mods.HalfTime,
        ["+Nightcore"] = Mods.Nightcore,
        ["+Flashlight"] = Mods.Flashlight,
        ["|Autoplay|"] = Mods.Autoplay,
        ["-SpunOut"] = Mods.SpunOut,
        ["~Autopilot~"] = Mods.Autopilot,
        ["+Perfect"] = Mods.Perfect,
        ["|Cinema|"] = Mods.Cinema,
        ["~Target~"] = Mods.Target,
        ["|1K|"] = Mods.Key1,
        ["|2K|"] = Mods.Key2,
        ["|3K|"] = Mods.Key3,
        ["|4K|"] = Mods.Key4,
        ["|5K|"] = Mods.Key5,
        ["|6K|"] = Mods.Key6,
        ["|7K|"] = Mods.Key7,
        ["|8K|"] = Mods.Key8,
        ["|9K|"] = Mods.Key9,
        ["|10K|"] = Mods.Key5 | Mods.KeyCoop,
        ["|12K|"] = Mods.Key6 | Mods.KeyCoop,
        ["|14K|"] = Mods.Key7 | Mods.KeyCoop,
        ["|16K|"] = Mods.Key8 | Mods.KeyCoop,
        ["|18K|"] = Mods.Key9 | Mods.KeyCoop,
    };

    /// <summary>Removes invalid mod combinations, mirroring app/constants/mods.py's filter_invalid_combos.</summary>
    public static Mods FilterInvalidCombos(this Mods mods, int modeVn)
    {
        var result = mods;

        // 1. mode-inspecific mod conflictions
        var dtNc = result & (Mods.DoubleTime | Mods.Nightcore);
        if (dtNc == (Mods.DoubleTime | Mods.Nightcore))
        {
            result &= ~Mods.DoubleTime; // DTNC
        }
        else if (dtNc != Mods.NoMod && (result & Mods.HalfTime) != Mods.NoMod)
        {
            result &= ~Mods.HalfTime; // (DT|NC)HT
        }

        if ((result & Mods.Easy) != Mods.NoMod && (result & Mods.HardRock) != Mods.NoMod)
        {
            result &= ~Mods.HardRock; // EZHR
        }

        if ((result & (Mods.NoFail | Mods.Relax | Mods.Autopilot)) != Mods.NoMod)
        {
            if ((result & Mods.SuddenDeath) != Mods.NoMod)
            {
                result &= ~Mods.SuddenDeath; // (NF|RX|AP)SD
            }

            if ((result & Mods.Perfect) != Mods.NoMod)
            {
                result &= ~Mods.Perfect; // (NF|RX|AP)PF
            }
        }

        if ((result & (Mods.Relax | Mods.Autopilot)) != Mods.NoMod && (result & Mods.NoFail) != Mods.NoMod)
        {
            result &= ~Mods.NoFail; // (RX|AP)NF
        }

        if ((result & Mods.Perfect) != Mods.NoMod && (result & Mods.SuddenDeath) != Mods.NoMod)
        {
            result &= ~Mods.SuddenDeath; // PFSD
        }

        // 2. remove mode-unique mods from incorrect gamemodes
        if (modeVn != 0) // osu! specific
        {
            result &= ~OsuSpecificMods;
        }

        // ctb & taiko have no unique mods

        if (modeVn != 3) // mania specific
        {
            result &= ~ManiaSpecificMods;
        }

        // 3. mode-specific mod conflictions
        if (modeVn == 0 && (result & Mods.Autopilot) != Mods.NoMod
            && (result & (Mods.SpunOut | Mods.Relax)) != Mods.NoMod)
        {
            result &= ~Mods.Autopilot; // (SO|RX)AP
        }

        if (modeVn == 3)
        {
            result &= ~Mods.Relax; // rx is std/taiko/ctb common
            if ((result & Mods.Hidden) != Mods.NoMod && (result & Mods.FadeIn) != Mods.NoMod)
            {
                result &= ~Mods.FadeIn; // HDFI
            }
        }

        // 4. remove multiple keymods, keeping only the first
        var keymodsUsed = result & KeyMods;
        if (CountSetBits(keymodsUsed) > 1)
        {
            var firstKeymod = Mods.NoMod;
            foreach (var candidate in new[]
                     {
                         Mods.Key1, Mods.Key2, Mods.Key3, Mods.Key4, Mods.Key5,
                         Mods.Key6, Mods.Key7, Mods.Key8, Mods.Key9,
                     })
            {
                if ((keymodsUsed & candidate) != Mods.NoMod)
                {
                    firstKeymod = candidate;
                    break;
                }
            }

            result &= ~(keymodsUsed & ~firstKeymod);
        }

        return result;
    }

    /// <summary>Parses a mod string like "HDDTRX" into two-character chunks. Ported from Mods.from_modstr.</summary>
    public static Mods FromModString(string s)
    {
        var mods = Mods.NoMod;

        for (var i = 0; i < s.Length; i += 2)
        {
            var chunk = s.Substring(i, Math.Min(2, s.Length - i)).ToUpperInvariant();
            if (ModStrToMod.TryGetValue(chunk, out var mod))
            {
                mods |= mod;
            }
        }

        return mods;
    }

    /// <summary>Parses a `/np`-style mod string (e.g. "+Hidden +DoubleTime"). Ported from Mods.from_np.</summary>
    public static Mods FromNowPlayingString(string s, int modeVn)
    {
        var mods = Mods.NoMod;

        foreach (var token in s.Split(' '))
        {
            if (NpStrToMod.TryGetValue(token, out var mod))
            {
                mods |= mod;
            }
        }

        return mods.FilterInvalidCombos(modeVn);
    }

    private static int CountSetBits(Mods value)
    {
        var count = 0;
        var v = (uint)value;
        while (v != 0)
        {
            count += (int)(v & 1);
            v >>= 1;
        }

        return count;
    }
}
