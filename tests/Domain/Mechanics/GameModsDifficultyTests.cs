using Basil.Domain.Mechanics;

namespace Basil.Domain.Tests.Mechanics;

/// <summary>Verifies the reduction of a play's mods to the ones that change the difficulty of the beatmap.</summary>
public class GameModsDifficultyTests
{
	/// <summary>Mods that only change how a play is scored or judged leave no trace in the reduction.</summary>
	[Theory]
	[InlineData(GameMods.NoFail)]
	[InlineData(GameMods.SuddenDeath)]
	[InlineData(GameMods.Perfect)]
	[InlineData(GameMods.Relax)]
	[InlineData(GameMods.SpunOut)]
	[InlineData(GameMods.ScoreV2)]
	[InlineData(GameMods.NoFail | GameMods.SpunOut | GameMods.SuddenDeath)]
	public void ScoringOnlyMods_ReduceToNoMod(GameMods mods)
	{
		Assert.Equal(GameMods.NoMod, mods.ReduceToDifficulty(GameMode.Standard));
	}

	/// <summary>Mods that change the difficulty are kept.</summary>
	[Theory]
	[InlineData(GameMods.Easy)]
	[InlineData(GameMods.HardRock)]
	[InlineData(GameMods.DoubleTime)]
	[InlineData(GameMods.HalfTime)]
	[InlineData(GameMods.Flashlight)]
	[InlineData(GameMods.HardRock | GameMods.Flashlight | GameMods.DoubleTime)]
	public void DifficultyMods_AreKept(GameMods mods)
	{
		Assert.Equal(mods, mods.ReduceToDifficulty(GameMode.Standard));
	}

	/// <summary>Nightcore and DoubleTime play at the same rate, so they share one reduction, in either wire form.</summary>
	[Theory]
	[InlineData(GameMods.Nightcore)]
	[InlineData(GameMods.DoubleTime | GameMods.Nightcore)]
	public void Nightcore_ReducesToDoubleTime(GameMods mods)
	{
		Assert.Equal(GameMods.DoubleTime, mods.ReduceToDifficulty(GameMode.Standard));
	}

	/// <summary>The mods a play drops as invalid do not count: EasyHardRock is Easy.</summary>
	[Fact]
	public void ConflictingMods_ReduceAsTheLegalCombination()
	{
		Assert.Equal(GameMods.Easy, (GameMods.Easy | GameMods.HardRock).ReduceToDifficulty(GameMode.Standard));
	}

	/// <summary>The key count is part of the difficulty in mania only.</summary>
	[Fact]
	public void KeyMod_CountsInManiaOnly()
	{
		Assert.Equal(GameMods.Key7, GameMods.Key7.ReduceToDifficulty(GameMode.Mania));
		Assert.Equal(GameMods.NoMod, GameMods.Key7.ReduceToDifficulty(GameMode.Standard));
		Assert.Equal(GameMods.NoMod, GameMods.Key7.ReduceToDifficulty(GameMode.Taiko));
	}

	/// <summary>Two plays that differ only in a scoring mod reduce to the same value.</summary>
	[Fact]
	public void PlaysThatDifferInScoringMods_ShareTheReduction()
	{
		var plain = (GameMods.DoubleTime | GameMods.Hidden).ReduceToDifficulty(GameMode.Standard);
		var noisy = (GameMods.DoubleTime | GameMods.Hidden | GameMods.NoFail | GameMods.SpunOut)
			.ReduceToDifficulty(GameMode.Standard);

		Assert.Equal(plain, noisy);
	}

	/// <summary>The reduction is stable: reducing a reduced value changes nothing.</summary>
	[Theory]
	[InlineData(GameMode.Standard, GameMods.Nightcore | GameMods.Hidden | GameMods.NoFail)]
	[InlineData(GameMode.Mania, GameMods.Key4 | GameMods.KeyCoop | GameMods.HalfTime | GameMods.Mirror)]
	public void Reduction_IsIdempotent(GameMode mode, GameMods mods)
	{
		var once = mods.ReduceToDifficulty(mode);

		Assert.Equal(once, once.ReduceToDifficulty(mode));
	}
}
