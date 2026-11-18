using System.Reflection;
using Basil.Domain.Game.Mechanics;
using Basil.Infrastructure.Beatmaps.Adapters;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;

namespace Basil.Infrastructure.Beatmaps.Tests.Adapters;

public sealed class DifficultyModsGuardTests
{
	[Theory]
	[InlineData(GameMode.Standard)]
	[InlineData(GameMode.Taiko)]
	[InlineData(GameMode.Catch)]
	[InlineData(GameMode.Mania)]
	public void EveryLazerDifficultyMod_IsInTheDifficultyGroup(GameMode mode)
	{
		var ruleset = OsuBeatmapAnalyser.CreateRuleset(mode);
		var calculator = ruleset.CreateDifficultyCalculator(new StreamlessWorkingBeatmap(
			new Beatmap { BeatmapInfo = { Ruleset = ruleset.RulesetInfo } }));
		var property = typeof(DifficultyCalculator).GetProperty("DifficultyAdjustmentMods",
			BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
		var adjusting = (Mod[])property.GetValue(calculator)!;
		Assert.NotEmpty(adjusting);

		foreach (var mod in adjusting)
		{
			var legacy = (GameMods)ruleset.ConvertToLegacyMods([mod]);
			Assert.True((legacy & ~GameModsGroups.Difficulty) == GameMods.NoMod,
				$"{mod.Acronym} ({legacy}) changes the difficulty in {mode} but is not in GameModsGroups.Difficulty.");
		}
	}
}
