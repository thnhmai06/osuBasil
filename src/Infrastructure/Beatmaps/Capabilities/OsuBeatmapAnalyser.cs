using System.Runtime.CompilerServices;
using Basil.Application.Beatmaps.Capabilities;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Domain.Beatmaps;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.Beatmaps.Legacy;
using osu.Game.IO;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Catch.Objects;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Taiko;
using osu.Game.Rulesets.Taiko.Objects;
using osu.Game.Utils;
using BeatmapDifficulty = Basil.Domain.Beatmaps.BeatmapDifficulty;
using LazerBeatmap = osu.Game.Beatmaps.Beatmap;

namespace Basil.Infrastructure.Beatmaps.Capabilities;

/// <summary>
///     Computes beatmap gameplay stats with ppy's osu!lazer ruleset libraries, the same engine the
///     real client and website run.
/// </summary>
/// <remarks>
///     The server uses these numbers for star rating and difficulty display only; they feed no
///     scoring, leaderboard, or match win condition.
/// </remarks>
internal sealed class OsuBeatmapAnalyser(IBeatmapsetStorage storage) : IBeatmapAnalyser
{
	// The content lives as long as the beatmap instance; it is decoded again when the beatmap's file changes.
	private readonly ConditionalWeakTable<BeatmapRecord, KeptContent> _contents = new();

	/// <inheritdoc />
	/// <exception cref="Exception">The beatmap file could not be decoded or analyzed.</exception>
	public BeatmapAnalysis Analyze(Stream content, GameMode mode, GameMods mods)
	{
		return Analyze(new StreamlessWorkingBeatmap(Decode(content)), mode, mods);
	}

	/// <inheritdoc />
	/// <exception cref="Exception">The beatmap file could not be decoded or analyzed.</exception>
	public async Task<BeatmapAnalysis?> AnalyzeAsync(BeatmapRecord beatmap, GameMods mods,
		CancellationToken cancellationToken = default)
	{
		var hash = beatmap.Hash;
		if (!_contents.TryGetValue(beatmap, out var kept) || kept.Hash != hash)
		{
			var osu = await storage.OpenAsync(beatmap.Beatmapset, BeatmapsetFileType.Version,
				$"{hash}.osu", cancellationToken);
			if (osu is null) return null;
			await using var osuScope = osu;

			var decoded = Decode(osu);
			kept = new KeptContent(hash, new StreamlessWorkingBeatmap(decoded));
			_contents.AddOrUpdate(beatmap, kept);
		}

		return Analyze(kept.Working, beatmap.Metadata.Difficulty.Mode, mods);
	}

	/// <summary>Decodes the content of a <c>.osu</c> file.</summary>
	private static LazerBeatmap Decode(Stream content)
	{
		using var reader = new LineBufferedReader(content, true);
		return Decoder.GetDecoder<LazerBeatmap>(reader).Decode(reader);
	}

	/// <summary>Computes the gameplay stats of an already-decoded difficulty under the given mode and mods.</summary>
	private static BeatmapAnalysis Analyze(IWorkingBeatmap working, GameMode mode, GameMods mods)
	{
		var ruleset = CreateRuleset(mode);

		var legacyMods = ruleset.ConvertFromLegacyMods((LegacyMods)mods).ToArray();
		var attributes = ruleset.CreateDifficultyCalculator(working).Calculate(legacyMods);
		var playable = working.GetPlayableBeatmap(ruleset.RulesetInfo, legacyMods);

		var displayDifficulty = ruleset.GetAdjustedDisplayDifficulty(working.Beatmap.BeatmapInfo, legacyMods);
		var maxCombo = playable.GetMaxCombo();
		var mostCommonBeatLength = playable.GetMostCommonBeatLength();
		var rate = ModUtils.CalculateRateWithMods(legacyMods);
		var bpm = mostCommonBeatLength > 0 ? 60000 / mostCommonBeatLength * rate : 0;
		var totalLength = TimeSpan.FromMilliseconds(playable.CalculatePlayableLength() / rate);

		var difficulty = new BeatmapDifficulty(
			mode, Round(bpm, 1), totalLength,
			displayDifficulty.CircleSize, displayDifficulty.ApproachRate,
			displayDifficulty.OverallDifficulty, displayDifficulty.DrainRate,
			Round(attributes.StarRating, 2));

		var objects = BuildObjectCounts(mode, playable.HitObjects, maxCombo);

		return new BeatmapAnalysis(difficulty, objects);
	}

	private static double Round(double value, int digits)
	{
		return Math.Round(value, digits, MidpointRounding.AwayFromZero);
	}

	/// <summary>
	///     Counts hit objects by concrete type into the per-mode <see cref="BeatmapObjects" /> subtype.
	/// </summary>
	/// <remarks>
	///     Standard, taiko, and mania count <paramref name="hitObjects" /> at the top level only:
	///     <c>playable.HitObjects</c> already excludes nested slider parts for standard, and taiko and
	///     mania have no equivalent container objects. Catch is the exception: <c>JuiceStream</c> and
	///     <c>BananaShower</c> are containers whose children only exist in
	///     <see cref="HitObject.NestedHitObjects" />, so counting recurses.
	/// </remarks>
	private static BeatmapObjects BuildObjectCounts(
		GameMode mode, IReadOnlyList<HitObject> hitObjects, int maxCombo)
	{
		switch (mode)
		{
			case GameMode.Standard:
			{
				int circles = 0, sliders = 0, spinners = 0;
				foreach (var hitObject in hitObjects)
					switch (hitObject)
					{
						case HitCircle: circles++; break;
						case Slider: sliders++; break;
						case Spinner: spinners++; break;
					}

				var counts = (OsuObjects)BeatmapObjects.Empty(mode);
				counts.Total = circles + sliders + spinners;
				counts.MaxCombo = maxCombo;
				counts.Circles = circles;
				counts.Sliders = sliders;
				counts.Spinners = spinners;
				return counts;
			}
			case GameMode.Taiko:
			{
				int hits = 0, drumRolls = 0, dendens = 0;
				foreach (var hitObject in hitObjects)
					switch (hitObject)
					{
						case DrumRoll: drumRolls++; break;
						case Swell: dendens++; break;
						case Hit: hits++; break;
					}

				var counts = (TaikoObjects)BeatmapObjects.Empty(mode);
				counts.Total = hits + drumRolls + dendens;
				counts.MaxCombo = maxCombo;
				counts.Hits = hits;
				counts.DrumRolls = drumRolls;
				counts.Dendens = dendens;
				return counts;
			}
			case GameMode.Catch:
			{
				int fruits = 0, droplets = 0, tinyDroplets = 0, bananas = 0;
				foreach (var hitObject in hitObjects)
					CountCatchRecursive(hitObject, ref fruits, ref droplets, ref tinyDroplets, ref bananas);

				var counts = (CatchObjects)BeatmapObjects.Empty(mode);
				counts.Total = fruits + droplets + tinyDroplets + bananas;
				counts.MaxCombo = maxCombo;
				counts.Fruits = fruits;
				counts.Droplets = droplets;
				counts.TinyDroplets = tinyDroplets;
				counts.Bananas = bananas;
				return counts;
			}
			case GameMode.Mania:
			{
				int notes = 0, holdNotes = 0;
				foreach (var hitObject in hitObjects)
					switch (hitObject)
					{
						case HoldNote: holdNotes++; break;
						case Note: notes++; break;
					}

				var counts = (ManiaObjects)BeatmapObjects.Empty(mode);
				counts.Total = notes + holdNotes;
				counts.MaxCombo = maxCombo;
				counts.Notes = notes;
				counts.HoldNotes = holdNotes;
				return counts;
			}
			default:
				throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown ruleset for game mode.");
		}
	}

	/// <summary>Recursively counts a Catch hit object and its nested parts into the per-type totals.</summary>
	private static void CountCatchRecursive(
		HitObject h, ref int fruits, ref int droplets, ref int tinyDroplets, ref int bananas)
	{
		switch (h)
		{
			case TinyDroplet: tinyDroplets++; break;
			case Droplet: droplets++; break;
			case Banana: bananas++; break;
			case Fruit: fruits++; break;
		}

		foreach (var nested in h.NestedHitObjects)
			CountCatchRecursive(nested, ref fruits, ref droplets, ref tinyDroplets, ref bananas);
	}

	/// <summary>Creates the osu!lazer ruleset instance for the given game mode.</summary>
	private static Ruleset CreateRuleset(GameMode mode)
	{
		return mode switch
		{
			GameMode.Standard => new OsuRuleset(),
			GameMode.Taiko => new TaikoRuleset(),
			GameMode.Catch => new CatchRuleset(),
			GameMode.Mania => new ManiaRuleset(),
			_ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown ruleset for game mode.")
		};
	}

	/// <summary>The decoded content of a beatmap's .osu file, with the hash of the file it was decoded from.</summary>
	private sealed record KeptContent(Md5 Hash, IWorkingBeatmap Working);
}