using System.Collections.Immutable;

namespace Basil.Domain.Game;

/// <summary>The modifiers a player can apply to a play; the bit values carry the game numbering.</summary>
[Flags]
public enum GameMods
{
	/// <summary>No modifier.</summary>
	NoMod = 0,

	/// <summary>The play cannot be failed: running out of health does not end it.</summary>
	NoFail = 1 << 0,

	/// <summary>Lowers the difficulty values and, in every mode except taiko, gives extra lives, for a reduced score multiplier.</summary>
	Easy = 1 << 1,

	/// <summary>Marks a play made on a touch screen.</summary>
	TouchScreen = 1 << 2, // old: 'NoVideo'

	/// <summary>Hit objects fade out as they approach, leaving less time to read them.</summary>
	Hidden = 1 << 3,

	/// <summary>Raises the difficulty values (and flips the playfield vertically in the standard mode), for a higher score multiplier.</summary>
	HardRock = 1 << 4,

	/// <summary>The play fails on the first miss.</summary>
	SuddenDeath = 1 << 5,

	/// <summary>Plays the beatmap at 1.5 times the speed.</summary>
	DoubleTime = 1 << 6,

	/// <summary>The game handles the clicking; the player only moves the cursor.</summary>
	Relax = 1 << 7,

	/// <summary>Plays the beatmap at 0.75 times the speed.</summary>
	HalfTime = 1 << 8,

	/// <summary>Plays the beatmap at 1.5 times the speed with a higher pitch; the game sets it together with <see cref="DoubleTime"/>.</summary>
	Nightcore = 1 << 9,

	/// <summary>Only a small area around the cursor is visible.</summary>
	Flashlight = 1 << 10,

	/// <summary>The game plays the beatmap by itself.</summary>
	Autoplay = 1 << 11,

	/// <summary>Spinners complete on their own (standard mode).</summary>
	SpunOut = 1 << 12,

	/// <summary>The game moves the cursor; the player only clicks (standard mode).</summary>
	Autopilot = 1 << 13,

	/// <summary>The play fails on anything but a perfect hit; the game sets it together with <see cref="SuddenDeath"/>.</summary>
	Perfect = 1 << 14,

	/// <summary>Plays mania with four key columns.</summary>
	Key4 = 1 << 15,

	/// <summary>Plays mania with five key columns.</summary>
	Key5 = 1 << 16,

	/// <summary>Plays mania with six key columns.</summary>
	Key6 = 1 << 17,

	/// <summary>Plays mania with seven key columns.</summary>
	Key7 = 1 << 18,

	/// <summary>Plays mania with eight key columns.</summary>
	Key8 = 1 << 19,

	/// <summary>Mania notes stay hidden until they approach the middle of the playfield.</summary>
	FadeIn = 1 << 20,

	/// <summary>Shuffles the key columns of the notes (mania).</summary>
	Random = 1 << 21,

	/// <summary>The game plays the beatmap by itself, showing its video and storyboard without the playfield.</summary>
	Cinema = 1 << 22,

	/// <summary>Target practice: replaces the hit objects with targets to hit as precisely as possible (standard mode).</summary>
	Target = 1 << 23,

	/// <summary>Plays mania with nine key columns.</summary>
	Key9 = 1 << 24,

	/// <summary>Two players share one beatmap, each with the key columns of the key count mod (mania).</summary>
	KeyCoop = 1 << 25,

	/// <summary>Plays mania with one key column.</summary>
	Key1 = 1 << 26,

	/// <summary>Plays mania with three key columns.</summary>
	Key3 = 1 << 27,

	/// <summary>Plays mania with two key columns.</summary>
	Key2 = 1 << 28,

	/// <summary>Scores the play with the second scoring system, which weighs accuracy and combo differently.</summary>
	ScoreV2 = 1 << 29,

	/// <summary>Flips the key columns of the notes horizontally (mania).</summary>
	Mirror = 1 << 30
}

/// <summary>Parses and validates sets of <see cref="GameMods"/>.</summary>
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

	extension(GameMods gameMods)
	{
		/// <summary>Removes the mods that cannot be used together or in the given mode.</summary>
		/// <param name="mode">The mode the mods are used in.</param>
		/// <returns>The mods that remain valid.</returns>
		/// <remarks>
		/// <see cref="GameMods.Nightcore"/> wins over <see cref="GameMods.DoubleTime"/>, and speed-up mods win over <see cref="GameMods.HalfTime"/>. <see cref="GameMods.Easy"/> wins over <see cref="GameMods.HardRock"/>. <see cref="GameMods.NoFail"/>, <see cref="GameMods.Relax"/> and <see cref="GameMods.Autopilot"/> remove <see cref="GameMods.SuddenDeath"/> and <see cref="GameMods.Perfect"/>, and Relax or Autopilot also remove NoFail. <see cref="GameMods.Autoplay"/> and <see cref="GameMods.Cinema"/> win over <see cref="GameMods.SuddenDeath"/>, <see cref="GameMods.Perfect"/>, <see cref="GameMods.Relax"/>, <see cref="GameMods.Autopilot"/> and <see cref="GameMods.SpunOut"/>. <see cref="GameMods.Perfect"/> wins over <see cref="GameMods.SuddenDeath"/>.
		/// <see cref="GameMods.Target"/> wins over every other mod, so it stays alone (in standard only, where it exists).
		/// Mods that belong to another mode are dropped (<see cref="GameModsGroups.OsuSpecific"/> outside standard, <see cref="GameModsGroups.ManiaSpecific"/> outside mania). In standard, <see cref="GameMods.Autopilot"/> is also dropped next to <see cref="GameMods.SpunOut"/> or <see cref="GameMods.Relax"/>; in mania, <see cref="GameMods.Relax"/> is dropped, <see cref="GameMods.FadeIn"/> next to <see cref="GameMods.Hidden"/>, and <see cref="GameMods.Hidden"/> and <see cref="GameMods.FadeIn"/> next to <see cref="GameMods.Flashlight"/>. When several key count mods are set, only one stays, taken in the order 4K to 9K, then 1K, 3K, 2K.
		/// </remarks>
		public GameMods RemoveInvalidMods(GameMode mode)
		{
			var result = gameMods;

			result = ResolveSpeedConflicts(result);
			result = ResolveFailConflicts(result);
			result = RemoveModeSpecific(result, mode);
			if ((result & GameMods.Target) != GameMods.NoMod) result = GameMods.Target; // every mod cancels out TP
			result = KeepFirstKeymod(result);

			return result;
		}

		/// <summary>Keeps only the mods that change how hard the beatmap is.</summary>
		/// <param name="mode">The mode the mods are used in.</param>
		/// <returns>The valid mods of <see cref="GameModsGroups.Difficulty"/>, with <see cref="GameMods.Nightcore"/> replaced by <see cref="GameMods.DoubleTime"/>.</returns>
		public GameMods ReduceToDifficulty(GameMode mode)
		{
			var reduced = gameMods.RemoveInvalidMods(mode) & GameModsGroups.Difficulty;
			return (reduced & GameMods.Nightcore) == GameMods.NoMod
				? reduced
				: (reduced & ~GameMods.Nightcore) | GameMods.DoubleTime;
		}

		/// <summary>Tells whether the mods can be used together in the given mode.</summary>
		/// <param name="mode">The mode the mods are used in.</param>
		/// <returns>True when removing the invalid mods would change nothing; <see cref="GameMods.DoubleTime"/> with <see cref="GameMods.Nightcore"/> and <see cref="GameMods.SuddenDeath"/> with <see cref="GameMods.Perfect"/> count as valid, because the game sets both.</returns>
		public bool IsValid(GameMode mode)
		{
			var canonical = Canonical(gameMods);
			return canonical == canonical.RemoveInvalidMods(mode);
		}

		/// <summary>Rejects mods that cannot be used together in the given mode.</summary>
		/// <param name="mode">The mode the mods are used in.</param>
		/// <exception cref="InvalidModsException">The mods are not valid for <paramref name="mode"/>.</exception>
		public void ThrowIfInvalid(GameMode mode)
		{
			if (!gameMods.IsValid(mode))
				throw new InvalidModsException();
		}
	}

	/// <summary>Reads mods from their two-letter acronyms written one after another, such as <c>HDDT</c>.</summary>
	/// <param name="s">The acronyms, in any letter case.</param>
	/// <returns>The mods named; unknown acronyms are ignored, and the result is not checked for conflicts.</returns>
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

	/// <summary>Reads mods from the space-separated tokens of a now-playing description, such as <c>+Hidden +DoubleTime</c>.</summary>
	/// <param name="s">The tokens, in any letter case.</param>
	/// <param name="mode">The mode the mods are used in.</param>
	/// <returns>The mods named, with those that are invalid for <paramref name="mode"/> removed; unknown tokens are ignored.</returns>
	public static GameMods FromNowPlayingString(string s, GameMode mode)
	{
		var mods = GameMods.NoMod;

		foreach (var token in s.Split(' '))
			if (NpStrToMod.TryGetValue(token, out var mod))
				mods |= mod;

		return mods.RemoveInvalidMods(mode);
	}

	// The osu! client always sends Nightcore with DoubleTime and Perfect with SuddenDeath, so these pairs are read as NC / PF, not as a conflict (bancho.py mods.py:65-67, 84-85).
	private static GameMods Canonical(GameMods mods)
	{
		if ((mods & (GameMods.DoubleTime | GameMods.Nightcore)) == (GameMods.DoubleTime | GameMods.Nightcore))
			mods &= ~GameMods.DoubleTime;

		if ((mods & (GameMods.Perfect | GameMods.SuddenDeath)) == (GameMods.Perfect | GameMods.SuddenDeath))
			mods &= ~GameMods.SuddenDeath;

		return mods;
	}

	private static GameMods ResolveSpeedConflicts(GameMods result)
	{
		// 1. mode-specific mod conflictions
		var dtNc = result & (GameMods.DoubleTime | GameMods.Nightcore);
		if (dtNc == (GameMods.DoubleTime | GameMods.Nightcore)) result &= ~GameMods.DoubleTime; // DTNC
		else if (dtNc != GameMods.NoMod && (result & GameMods.HalfTime) != GameMods.NoMod)
			result &= ~GameMods.HalfTime; // (DT|NC)HT

		if ((result & GameMods.Easy) != GameMods.NoMod && (result & GameMods.HardRock) != GameMods.NoMod)
			result &= ~GameMods.HardRock; // EZHR

		return result;
	}

	private static GameMods ResolveFailConflicts(GameMods result)
	{
		if ((result & (GameMods.Autoplay | GameMods.Cinema)) != GameMods.NoMod)
			result &= ~(GameMods.SuddenDeath | GameMods.Perfect | GameMods.Relax | GameMods.Autopilot |
			            GameMods.SpunOut); // (AU|CN)(SD|PF|RX|AP|SO)

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

		return result;
	}

	private static GameMods RemoveModeSpecific(GameMods result, GameMode mode)
	{
		// 2. remove mode-unique mods from incorrect gamemodes
		if (mode != GameMode.Standard) // osu! specific
			result &= ~GameModsGroups.OsuSpecific;

		// ctb & taiko have no unique mods
		if (mode != GameMode.Mania) // mania specific
			result &= ~GameModsGroups.ManiaSpecific;

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
				if ((result & GameMods.Flashlight) != GameMods.NoMod)
					result &= ~(GameMods.Hidden | GameMods.FadeIn); // (HD|FI)FL
				if ((result & GameMods.Hidden) != GameMods.NoMod &&
				    (result & GameMods.FadeIn) != GameMods.NoMod) result &= ~GameMods.FadeIn; // HDFI
				break;
			}
		}

		return result;
	}

	private static GameMods KeepFirstKeymod(GameMods result)
	{
		// 4. remove multiple keymods, keeping only the first
		var keymodsUsed = result & GameModsGroups.Key;
		if (CountSetBits(keymodsUsed) > 1)
		{
			var firstKeymod =
				new[]
				{
					GameMods.Key4, GameMods.Key5, GameMods.Key6, GameMods.Key7, GameMods.Key8, GameMods.Key9,
					GameMods.Key1, GameMods.Key3, GameMods.Key2
				}.FirstOrDefault(candidate => (keymodsUsed & candidate) != GameMods.NoMod);

			result &= ~(keymodsUsed & ~firstKeymod);
		}

		return result;
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

/// <summary>Sets of mods that share a role.</summary>
public static class GameModsGroups
{
	/// <summary>The mods that change the speed of the beatmap.</summary>
	public const GameMods SpeedChanging = GameMods.DoubleTime | GameMods.Nightcore | GameMods.HalfTime;

	/// <summary>The mods that set the number of mania key columns.</summary>
	public const GameMods Key = GameMods.Key1 | GameMods.Key2 | GameMods.Key3 | GameMods.Key4 | GameMods.Key5 |
	                            GameMods.Key6 | GameMods.Key7 | GameMods.Key8 | GameMods.Key9;

	/// <summary>The mods that only exist in the standard mode.</summary>
	public const GameMods OsuSpecific = GameMods.Autopilot | GameMods.SpunOut | GameMods.Target;

	/// <summary>The mods that only exist in the mania mode.</summary>
	public const GameMods ManiaSpecific = GameMods.Mirror | GameMods.Random | GameMods.FadeIn | GameMods.KeyCoop | Key;

	/// <summary>The mods that change how hard a beatmap is, and so its star rating.</summary>
	public const GameMods Difficulty = GameMods.Easy | GameMods.HardRock | GameMods.DoubleTime | GameMods.Nightcore |
	                                   GameMods.HalfTime | GameMods.Flashlight | GameMods.Hidden |
	                                   GameMods.TouchScreen | GameMods.KeyCoop | Key;
}