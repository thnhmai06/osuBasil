using System.Collections.Immutable;

namespace Basil.Domain.Mechanics;

/// <summary>
///     Represents the gameplay modifier flags an osu! client can apply to a play.
/// </summary>
/// <remarks>
///     A bitwise combination of the individual mods. The key mods only apply to osu!mania, and
///     several mods apply to specific game modes only.
/// </remarks>
[Flags]
public enum GameMods
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
	Mirror = 1 << 30,

	// Groups
	/// <summary>The mods that change the playback rate of the beatmap.</summary>
	SpeedChangingMods = DoubleTime | Nightcore | HalfTime,

	/// <summary>The mods that restrict the play to a specific key count.</summary>
	KeyMods = Key1 | Key2 | Key3 | Key4 | Key5 | Key6 | Key7 | Key8 | Key9,

	/// <summary>The mods that only apply to osu!standard.</summary>
	OsuSpecificMods = Autopilot | SpunOut | Target,

	/// <summary>The mods that only apply to osu!mania.</summary>
	ManiaSpecificMods = Mirror | Random | FadeIn | KeyMods
}

/// <summary>
///     Provides the business rules for combining and parsing <see cref="GameMods" /> values.
/// </summary>
public static class ModsExtensions
{
	private static readonly ImmutableDictionary<string, GameMods> ModStrToMod = ImmutableDictionary.CreateRange(
		StringComparer.OrdinalIgnoreCase, new Dictionary<string, GameMods>
		{
			["NF"] = GameMods.NoFail,
			["EZ"] = GameMods.Easy,
			["TD"] = GameMods.TouchScreen,
			["HD"] = GameMods.Hidden,
			["HR"] = GameMods.HardRock,
			["SD"] = GameMods.SuddenDeath,
			["DT"] = GameMods.DoubleTime,
			["RX"] = GameMods.Relax,
			["HT"] = GameMods.HalfTime,
			["NC"] = GameMods.Nightcore,
			["FL"] = GameMods.Flashlight,
			["AU"] = GameMods.Autoplay,
			["SO"] = GameMods.SpunOut,
			["AP"] = GameMods.Autopilot,
			["PF"] = GameMods.Perfect,
			["FI"] = GameMods.FadeIn,
			["RN"] = GameMods.Random,
			["CN"] = GameMods.Cinema,
			["TP"] = GameMods.Target,
			["V2"] = GameMods.ScoreV2,
			["MR"] = GameMods.Mirror,
			["1K"] = GameMods.Key1,
			["2K"] = GameMods.Key2,
			["3K"] = GameMods.Key3,
			["4K"] = GameMods.Key4,
			["5K"] = GameMods.Key5,
			["6K"] = GameMods.Key6,
			["7K"] = GameMods.Key7,
			["8K"] = GameMods.Key8,
			["9K"] = GameMods.Key9,
			["CO"] = GameMods.KeyCoop
		});

	private static readonly ImmutableDictionary<string, GameMods> NpStrToMod = ImmutableDictionary.CreateRange(
		StringComparer.OrdinalIgnoreCase, new Dictionary<string, GameMods>
		{
			["-NoFail"] = GameMods.NoFail,
			["-Easy"] = GameMods.Easy,
			["+Hidden"] = GameMods.Hidden,
			["+HardRock"] = GameMods.HardRock,
			["+SuddenDeath"] = GameMods.SuddenDeath,
			["+DoubleTime"] = GameMods.DoubleTime,
			["~Relax~"] = GameMods.Relax,
			["-HalfTime"] = GameMods.HalfTime,
			["+Nightcore"] = GameMods.Nightcore,
			["+Flashlight"] = GameMods.Flashlight,
			["|Autoplay|"] = GameMods.Autoplay,
			["-SpunOut"] = GameMods.SpunOut,
			["~Autopilot~"] = GameMods.Autopilot,
			["+Perfect"] = GameMods.Perfect,
			["|Cinema|"] = GameMods.Cinema,
			["~Target~"] = GameMods.Target,
			["|1K|"] = GameMods.Key1,
			["|2K|"] = GameMods.Key2,
			["|3K|"] = GameMods.Key3,
			["|4K|"] = GameMods.Key4,
			["|5K|"] = GameMods.Key5,
			["|6K|"] = GameMods.Key6,
			["|7K|"] = GameMods.Key7,
			["|8K|"] = GameMods.Key8,
			["|9K|"] = GameMods.Key9,
			["|10K|"] = GameMods.Key5 | GameMods.KeyCoop,
			["|12K|"] = GameMods.Key6 | GameMods.KeyCoop,
			["|14K|"] = GameMods.Key7 | GameMods.KeyCoop,
			["|16K|"] = GameMods.Key8 | GameMods.KeyCoop,
			["|18K|"] = GameMods.Key9 | GameMods.KeyCoop
		});

	/// <summary>
	///     Removes invalid mod combinations, leaving only the legal ones.
	/// </summary>
	/// <param name="gameMods">The mod combination to filter.</param>
	/// <param name="mode">The gamemode used.</param>
	/// <returns>The filtered mod combination.</returns>
	/// <remarks>
	///     Resolves conflicts between speed mods, drops mods that do not apply to the given mode,
	///     and keeps only the first key mod when several are set.
	/// </remarks>
	public static GameMods RemoveInvalidMods(this GameMods gameMods, GameMode mode)
	{
		var result = gameMods;

		// 1. mode-specific mod conflictions
		var dtNc = result & (GameMods.DoubleTime | GameMods.Nightcore);
		if (dtNc == (GameMods.DoubleTime | GameMods.Nightcore)) result &= ~GameMods.DoubleTime; // DTNC
		else if (dtNc != GameMods.NoMod && (result & GameMods.HalfTime) != GameMods.NoMod)
			result &= ~GameMods.HalfTime; // (DT|NC)HT

		if ((result & GameMods.Easy) != GameMods.NoMod && (result & GameMods.HardRock) != GameMods.NoMod)
			result &= ~GameMods.HardRock; // EZHR

		if ((result & (GameMods.NoFail | GameMods.Relax | GameMods.Autopilot)) != GameMods.NoMod)
		{
			if ((result & GameMods.SuddenDeath) != GameMods.NoMod) result &= ~GameMods.SuddenDeath; // (NF|RX|AP)SD
			if ((result & GameMods.Perfect) != GameMods.NoMod) result &= ~GameMods.Perfect; // (NF|RX|AP)PF
		}

		if ((result & (GameMods.Relax | GameMods.Autopilot)) != GameMods.NoMod &&
		    (result & GameMods.NoFail) != GameMods.NoMod)
			result &= ~GameMods.NoFail; // (RX|AP)NF

		if ((result & GameMods.Perfect) != GameMods.NoMod && (result & GameMods.SuddenDeath) != GameMods.NoMod)
			result &= ~GameMods.SuddenDeath; // PFSD

		// 2. remove mode-unique mods from incorrect gamemodes
		if (mode != GameMode.Standard) // osu! specific
			result &= ~GameMods.OsuSpecificMods;

		// ctb & taiko have no unique mods
		if (mode != GameMode.Mania) // mania specific
			result &= ~GameMods.ManiaSpecificMods;

		switch (mode)
		{
			// 3. mode-specific mod conflictions
			case GameMode.Standard when (result & GameMods.Autopilot) != GameMods.NoMod
			                            && (result & (GameMods.SpunOut | GameMods.Relax)) != GameMods.NoMod:
				result &= ~GameMods.Autopilot; // (SO|RX)AP
				break;
			case GameMode.Mania:
			{
				result &= ~GameMods.Relax; // rx is std/taiko/ctb common
				if ((result & GameMods.Hidden) != GameMods.NoMod &&
				    (result & GameMods.FadeIn) != GameMods.NoMod) result &= ~GameMods.FadeIn; // HDFI
				break;
			}
		}

		// 4. remove multiple keymods, keeping only the first
		var keymodsUsed = result & GameMods.KeyMods;
		if (CountSetBits(keymodsUsed) > 1)
		{
			var firstKeymod =
				new[]
				{
					GameMods.Key1, GameMods.Key2, GameMods.Key3, GameMods.Key4, GameMods.Key5, GameMods.Key6,
					GameMods.Key7, GameMods.Key8, GameMods.Key9
				}.FirstOrDefault(candidate => (keymodsUsed & candidate) != GameMods.NoMod);

			result &= ~(keymodsUsed & ~firstKeymod);
		}

		return result;
	}

	/// <summary>
	///     Parses a mod string of two-character chunks into a <see cref="GameMods" /> value.
	/// </summary>
	/// <param name="s">The mod string, for example, "HDDTRX".</param>
	/// <returns>
	///     The parsed mod combination. Chunks that are not recognized mod codes are ignored.
	/// </returns>
	public static GameMods FromModString(string s)
	{
		var mods = GameMods.NoMod;

		for (var i = 0; i < s.Length; i += 2)
		{
			var chunk = s.Substring(i, Math.Min(2, s.Length - i)).ToUpperInvariant();
			if (ModStrToMod.TryGetValue(chunk, out var mod)) mods |= mod;
		}

		return mods;
	}

	/// <summary>
	///     Parses a now-playing mod string into a <see cref="GameMods" /> value.
	/// </summary>
	/// <param name="s">The space-delimited now-playing mod string, for example, "+Hidden +DoubleTime".</param>
	/// <param name="mode">The gamemode used to filter the parsed mods.</param>
	/// <returns>The parsed mod combination, filtered for the given mode.</returns>
	public static GameMods FromNowPlayingString(string s, GameMode mode)
	{
		var mods = GameMods.NoMod;

		foreach (var token in s.Split(' '))
			if (NpStrToMod.TryGetValue(token, out var mod))
				mods |= mod;

		return mods.RemoveInvalidMods(mode);
	}

	private static int CountSetBits(GameMods value)
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