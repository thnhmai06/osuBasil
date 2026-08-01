using System.Collections.Immutable;

namespace Basil.Domain.Scores;

/// <summary>
///     Represents the gameplay modifier flags an osu! client can apply to a play.
/// </summary>
/// <remarks>
///     A bitwise combination of the individual mods. The key mods only apply to osu!mania, and
///     several mods apply to specific game modes only.
/// </remarks>
[Flags]
public enum Mods : uint
{
	/// <summary>No mod is applied.</summary>
	NoMod = 0,

	/// <summary>Prevents the play from failing on a miss.</summary>
	NoFail = 1 << 0,

	/// <summary>Makes the beatmap easier to play.</summary>
	Easy = 1 << 1,

	/// <summary>Enables touch screen input. The old name for this mod was NoVideo.</summary>
	TouchScreen = 1 << 2, // old: 'NoVideo'

	/// <summary>Fades the hit objects out shortly before they are hit.</summary>
	Hidden = 1 << 3,

	/// <summary>Makes the beatmap harder to play.</summary>
	HardRock = 1 << 4,

	/// <summary>Fails the play on the first miss.</summary>
	SuddenDeath = 1 << 5,

	/// <summary>Speeds the beatmap up.</summary>
	DoubleTime = 1 << 6,

	/// <summary>Allows the play to be completed without clicking the hit objects.</summary>
	Relax = 1 << 7,

	/// <summary>Slows the beatmap down.</summary>
	HalfTime = 1 << 8,

	/// <summary>Applies the DoubleTime speed change together with a pitch shift.</summary>
	Nightcore = 1 << 9,

	/// <summary>Limits the visible area around the cursor.</summary>
	Flashlight = 1 << 10,

	/// <summary>Plays the beatmap automatically.</summary>
	Autoplay = 1 << 11,

	/// <summary>Automatically completes spinners.</summary>
	SpunOut = 1 << 12,

	/// <summary>Automates the cursor, leaving only the clicks to the player.</summary>
	Autopilot = 1 << 13,

	/// <summary>Fails the play on the first non-300 judgment.</summary>
	Perfect = 1 << 14,

	/// <summary>Restricts the play to four keys.</summary>
	Key4 = 1 << 15,

	/// <summary>Restricts the play to five keys.</summary>
	Key5 = 1 << 16,

	/// <summary>Restricts the play to six keys.</summary>
	Key6 = 1 << 17,

	/// <summary>Restricts the play to seven keys.</summary>
	Key7 = 1 << 18,

	/// <summary>Restricts the play to eight keys.</summary>
	Key8 = 1 << 19,

	/// <summary>Fades the notes in during the play.</summary>
	FadeIn = 1 << 20,

	/// <summary>Randomizes the column layout of the notes.</summary>
	Random = 1 << 21,

	/// <summary>Plays the beatmap as a cinematic without gameplay.</summary>
	Cinema = 1 << 22,

	/// <summary>Shows a target score the player should aim for.</summary>
	Target = 1 << 23,

	/// <summary>Restricts the play to nine keys.</summary>
	Key9 = 1 << 24,

	/// <summary>Combines two key counts for cooperative play.</summary>
	KeyCoop = 1 << 25,

	/// <summary>Restricts the play to one key.</summary>
	Key1 = 1 << 26,

	/// <summary>Restricts the play to three keys.</summary>
	Key3 = 1 << 27,

	/// <summary>Restricts the play to two keys.</summary>
	Key2 = 1 << 28,

	/// <summary>Uses the ScoreV2 scoring rules.</summary>
	ScoreV2 = 1 << 29,

	/// <summary>Mirrors the column layout of the notes.</summary>
	Mirror = 1 << 30
}

/// <summary>
///     Provides the business rules for combining and parsing <see cref="Mods" /> values.
/// </summary>
public static class ModsExtensions
{
	/// <summary>
	///     The mods that change the speed of the beatmap.
	/// </summary>
	/// <remarks>
	///     Used by multiplayer's freemods setting to split the applied mods into match-wide and
	///     per-slot groups.
	/// </remarks>
	public const Mods SpeedChangingMods = Mods.DoubleTime | Mods.Nightcore | Mods.HalfTime;

	private const Mods KeyMods = Mods.Key1 | Mods.Key2 | Mods.Key3 | Mods.Key4 | Mods.Key5
	                             | Mods.Key6 | Mods.Key7 | Mods.Key8 | Mods.Key9;

	private const Mods OsuSpecificMods = Mods.Autopilot | Mods.SpunOut | Mods.Target;
	private const Mods ManiaSpecificMods = Mods.Mirror | Mods.Random | Mods.FadeIn | KeyMods;

	private static readonly ImmutableDictionary<string, Mods> ModStrToMod = ImmutableDictionary.CreateRange(
		StringComparer.OrdinalIgnoreCase, new Dictionary<string, Mods>
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
			["CO"] = Mods.KeyCoop
		});

	private static readonly ImmutableDictionary<string, Mods> NpStrToMod = ImmutableDictionary.CreateRange(
		StringComparer.OrdinalIgnoreCase, new Dictionary<string, Mods>
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
			["|18K|"] = Mods.Key9 | Mods.KeyCoop
		});

	/// <summary>
	///     Removes invalid mod combinations, leaving only the legal ones.
	/// </summary>
	/// <param name="mods">The mod combination to filter.</param>
	/// <param name="modeVn">
	///     The version number of the game mode: 0 for standard, 1 for taiko, 2 for catch, and 3 for
	///     mania.
	/// </param>
	/// <returns>The filtered mod combination.</returns>
	/// <remarks>
	///     Resolves conflicts between speed mods, drops mods that do not apply to the given mode,
	///     and keeps only the first key mod when several are set.
	/// </remarks>
	public static Mods FilterInvalidCombos(this Mods mods, int modeVn)
	{
		var result = mods;

		// 1. mode-specific mod conflictions
		var dtNc = result & (Mods.DoubleTime | Mods.Nightcore);
		if (dtNc == (Mods.DoubleTime | Mods.Nightcore)) result &= ~Mods.DoubleTime; // DTNC
		else if (dtNc != Mods.NoMod && (result & Mods.HalfTime) != Mods.NoMod) result &= ~Mods.HalfTime; // (DT|NC)HT

		if ((result & Mods.Easy) != Mods.NoMod && (result & Mods.HardRock) != Mods.NoMod)
			result &= ~Mods.HardRock; // EZHR

		if ((result & (Mods.NoFail | Mods.Relax | Mods.Autopilot)) != Mods.NoMod)
		{
			if ((result & Mods.SuddenDeath) != Mods.NoMod) result &= ~Mods.SuddenDeath; // (NF|RX|AP)SD
			if ((result & Mods.Perfect) != Mods.NoMod) result &= ~Mods.Perfect; // (NF|RX|AP)PF
		}

		if ((result & (Mods.Relax | Mods.Autopilot)) != Mods.NoMod && (result & Mods.NoFail) != Mods.NoMod)
			result &= ~Mods.NoFail; // (RX|AP)NF

		if ((result & Mods.Perfect) != Mods.NoMod && (result & Mods.SuddenDeath) != Mods.NoMod)
			result &= ~Mods.SuddenDeath; // PFSD

		// 2. remove mode-unique mods from incorrect gamemodes
		if (modeVn != 0) // osu! specific
			result &= ~OsuSpecificMods;

		// ctb & taiko have no unique mods
		if (modeVn != 3) // mania specific
			result &= ~ManiaSpecificMods;

		switch (modeVn)
		{
			// 3. mode-specific mod conflictions
			case 0 when (result & Mods.Autopilot) != Mods.NoMod
			            && (result & (Mods.SpunOut | Mods.Relax)) != Mods.NoMod:
				result &= ~Mods.Autopilot; // (SO|RX)AP
				break;
			case 3:
			{
				result &= ~Mods.Relax; // rx is std/taiko/ctb common
				if ((result & Mods.Hidden) != Mods.NoMod &&
				    (result & Mods.FadeIn) != Mods.NoMod) result &= ~Mods.FadeIn; // HDFI
				break;
			}
		}

		// 4. remove multiple keymods, keeping only the first
		var keymodsUsed = result & KeyMods;
		if (CountSetBits(keymodsUsed) > 1)
		{
			var firstKeymod =
				new[]
				{
					Mods.Key1, Mods.Key2, Mods.Key3, Mods.Key4, Mods.Key5, Mods.Key6, Mods.Key7, Mods.Key8, Mods.Key9
				}.FirstOrDefault(candidate => (keymodsUsed & candidate) != Mods.NoMod);

			result &= ~(keymodsUsed & ~firstKeymod);
		}

		return result;
	}

	/// <summary>
	///     Parses a mod string of two-character chunks into a <see cref="Mods" /> value.
	/// </summary>
	/// <param name="s">The mod string, for example "HDDTRX".</param>
	/// <returns>
	///     The parsed mod combination. Chunks that are not recognized mod codes are ignored.
	/// </returns>
	public static Mods FromModString(string s)
	{
		var mods = Mods.NoMod;

		for (var i = 0; i < s.Length; i += 2)
		{
			var chunk = s.Substring(i, Math.Min(2, s.Length - i)).ToUpperInvariant();
			if (ModStrToMod.TryGetValue(chunk, out var mod)) mods |= mod;
		}

		return mods;
	}

	/// <summary>
	///     Parses a now-playing mod string into a <see cref="Mods" /> value.
	/// </summary>
	/// <param name="s">The space-delimited now-playing mod string, for example "+Hidden +DoubleTime".</param>
	/// <param name="modeVn">
	///     The version number of the game mode, used to filter out invalid combinations.
	/// </param>
	/// <returns>The parsed mod combination, filtered for the given mode.</returns>
	public static Mods FromNowPlayingString(string s, int modeVn)
	{
		var mods = Mods.NoMod;

		foreach (var token in s.Split(' '))
			if (NpStrToMod.TryGetValue(token, out var mod))
				mods |= mod;

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