using System.Collections.Immutable;

namespace Basil.Domain.Mechanics;

[Flags]
public enum GameMods
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

	Mirror = 1 << 30
}

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
		public GameMods RemoveInvalidMods(GameMode mode)
		{
			var result = gameMods;

			result = ResolveSpeedConflicts(result);
			result = ResolveFailConflicts(result);
			result = RemoveModeSpecific(result, mode);
			result = KeepFirstKeymod(result);

			return result;
		}

		public GameMods ReduceToDifficulty(GameMode mode)
		{
			var reduced = gameMods.RemoveInvalidMods(mode) & GameModsGroups.Difficulty;
			return (reduced & GameMods.Nightcore) == GameMods.NoMod
				? reduced
				: (reduced & ~GameMods.Nightcore) | GameMods.DoubleTime;
		}

		public bool IsValid(GameMode mode)
		{
			var canonical = Canonical(gameMods);
			return canonical == canonical.RemoveInvalidMods(mode);
		}

		public void ThrowIfInvalid(GameMode mode)
		{
			if (!gameMods.IsValid(mode))
				throw new InvalidModsException();
		}
	}

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

	public static GameMods FromNowPlayingString(string s, GameMode mode)
	{
		var mods = GameMods.NoMod;

		foreach (var token in s.Split(' '))
			if (NpStrToMod.TryGetValue(token, out var mod))
				mods |= mod;

		return mods.RemoveInvalidMods(mode);
	}

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
					GameMods.Key1, GameMods.Key2, GameMods.Key3, GameMods.Key4, GameMods.Key5, GameMods.Key6,
					GameMods.Key7, GameMods.Key8, GameMods.Key9
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

public static class GameModsGroups
{
	public const GameMods SpeedChanging = GameMods.DoubleTime | GameMods.Nightcore | GameMods.HalfTime;

	public const GameMods Key = GameMods.Key1 | GameMods.Key2 | GameMods.Key3 | GameMods.Key4 | GameMods.Key5 |
	                            GameMods.Key6 | GameMods.Key7 | GameMods.Key8 | GameMods.Key9;

	public const GameMods OsuSpecific = GameMods.Autopilot | GameMods.SpunOut | GameMods.Target;

	public const GameMods ManiaSpecific = GameMods.Mirror | GameMods.Random | GameMods.FadeIn | GameMods.KeyCoop | Key;

	public const GameMods Difficulty = GameMods.Easy | GameMods.HardRock | GameMods.DoubleTime | GameMods.Nightcore |
	                                   GameMods.HalfTime | GameMods.Flashlight | GameMods.Hidden |
	                                   GameMods.TouchScreen | GameMods.KeyCoop | Key;
}