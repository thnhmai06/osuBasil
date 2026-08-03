using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using osu.Framework.Audio.Track;
using osu.Framework.Extensions;
using osu.Framework.Graphics.Textures;
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
using osu.Game.Skinning;
using osu.Game.Utils;
using Beatmap = osu.Game.Beatmaps.Beatmap;
using GameMode = Basil.Domain.Beatmaps.GameMode;

namespace Basil.Infrastructure.Performance;

/// <inheritdoc cref="IOsuCalculator" />
/// <remarks>
///     Uses ppy's own osu!lazer ruleset libraries, the same engine the real client and website run,
///     by referencing the NuGet packages directly. The sibling osu-difficulty-calculator repo is a
///     batch CLI orchestrator around those same packages, not a callable server, so shelling out to
///     it is neither possible nor needed.
/// </remarks>
public sealed class PpyOsuCalculator : IOsuCalculator
{
	/// <inheritdoc cref="IOsuCalculator.Analyze" />
	/// <exception cref="InvalidOperationException">The beatmap file could not be decoded or analyzed.</exception>
	public BeatmapAnalysis Analyze(string beatmapFilePath, GameMode mode, Mods mods)
	{
		try
		{
			using var stream = File.OpenRead(beatmapFilePath);
			using var reader = new LineBufferedReader(stream);
			var beatmap = Decoder.GetDecoder<Beatmap>(reader).Decode(reader);

			var ruleset = CreateRuleset(mode);
			var workingBeatmap = new StreamlessWorkingBeatmap(beatmap);

			// var strippedMods = mods & ~(Mods.Relax | Mods.Autopilot);
			// var legacyMods = ruleset.ConvertFromLegacyMods((LegacyMods)strippedMods).ToArray();
			var legacyMods = ruleset.ConvertFromLegacyMods((LegacyMods)mods).ToArray();
			var attributes = ruleset.CreateDifficultyCalculator(workingBeatmap).Calculate(legacyMods);
			var playable = workingBeatmap.GetPlayableBeatmap(ruleset.RulesetInfo, legacyMods);

			var displayDifficulty = ruleset.GetAdjustedDisplayDifficulty(beatmap.BeatmapInfo, legacyMods);
			var maxCombo = playable.GetMaxCombo();
			var mostCommonBeatLength = playable.GetMostCommonBeatLength();
			var rate = ModUtils.CalculateRateWithMods(legacyMods);
			var bpm = mostCommonBeatLength > 0 ? 60000 / mostCommonBeatLength * rate : 0;
			var totalLength = TimeSpan.FromMilliseconds(playable.CalculatePlayableLength() / rate);

			var difficulty = new Difficulty(
				mode, Round(bpm, 1), totalLength,
				Round(displayDifficulty.CircleSize, 1), Round(displayDifficulty.ApproachRate, 1),
				Round(displayDifficulty.OverallDifficulty, 1), Round(displayDifficulty.DrainRate, 1),
				Round(attributes.StarRating, 2));

			var objectCounts = BuildObjectCounts(mode, playable.HitObjects, maxCombo);

			return new BeatmapAnalysis(difficulty, objectCounts);
		}
		catch (Exception e)
		{
			throw new InvalidOperationException($"Failed to analyze beatmap '{beatmapFilePath}'.", e);
		}
	}

	/// <inheritdoc cref="IOsuCalculator.ComputeBeatmapMd5" />
	public string ComputeBeatmapMd5(byte[] beatmapBytes)
	{
		using var stream = new MemoryStream(beatmapBytes);
		return stream.ComputeMD5Hash();
	}

	private static double Round(double value, int digits)
	{
		return Math.Round(value, digits, MidpointRounding.AwayFromZero);
	}

	/// <summary>
	///     Counts hit objects by concrete type into the per-mode <see cref="BeatmapObjectCounts" />
	///     subtype.
	/// </summary>
	/// <remarks>
	///     - <see cref="GameMode.Standard" />/<see cref="GameMode.Taiko" />/<see cref="GameMode.Mania" />
	///     count <paramref name="hitObjects" /> at the top level only: <c>playable.HitObjects</c> already exclude
	///     nested slider parts for <see cref="GameMode.Standard" />, and
	///     <see cref="GameMode.Taiko" />/<see cref="GameMode.Mania" /> have no equivalent container objects.
	///     - <see cref="GameMode.Catch" /> is the exception: <c>JuiceStream</c> and <c>BananaShower</c> are containers
	///     whose Droplet/TinyDroplet/Banana children only exist in <see cref="HitObject.NestedHitObjects" />,
	///     so counting recurses. <see cref="CatchBeatmapObjectCounts.TinyDroplets" /> is checked before
	///     <see cref="CatchBeatmapObjectCounts.Droplets" /> since <c>TinyDroplet</c> derives from <c>Droplet</c>
	/// </remarks>
	private static BeatmapObjectCounts BuildObjectCounts(
		GameMode mode, IReadOnlyList<HitObject> hitObjects, int maxCombo)
	{
		switch (mode)
		{
			case GameMode.Standard:
			{
				int circles = 0, sliders = 0, spinners = 0;
				foreach (var h in hitObjects)
					switch (h)
					{
						case HitCircle: circles++; break;
						case Slider: sliders++; break;
						case Spinner: spinners++; break;
					}

				return new OsuBeatmapObjectCounts
				{
					Total = circles + sliders + spinners, MaxCombo = maxCombo,
					Circles = circles, Sliders = sliders, Spinners = spinners
				};
			}
			case GameMode.Taiko:
			{
				int hits = 0, drumRolls = 0, dendens = 0;
				foreach (var h in hitObjects)
					switch (h)
					{
						case DrumRoll: drumRolls++; break;
						case Swell: dendens++; break;
						case Hit: hits++; break;
					}

				return new TaikoBeatmapObjectCounts
				{
					Total = hits + drumRolls + dendens, MaxCombo = maxCombo,
					Hits = hits, DrumRolls = drumRolls, Dendens = dendens
				};
			}
			case GameMode.Catch:
			{
				int fruits = 0, droplets = 0, tinyDroplets = 0, bananas = 0;
				foreach (var h in hitObjects)
					CountCatchRecursive(h, ref fruits, ref droplets, ref tinyDroplets, ref bananas);

				return new CatchBeatmapObjectCounts
				{
					Total = fruits + droplets + tinyDroplets + bananas, MaxCombo = maxCombo,
					Fruits = fruits, Droplets = droplets, TinyDroplets = tinyDroplets, Bananas = bananas
				};
			}
			case GameMode.Mania:
			{
				int notes = 0, holdNotes = 0;
				foreach (var h in hitObjects)
					switch (h)
					{
						case HoldNote: holdNotes++; break;
						case Note: notes++; break;
					}

				return new ManiaBeatmapObjectCounts
				{
					Total = notes + holdNotes, MaxCombo = maxCombo, Notes = notes, HoldNotes = holdNotes
				};
			}
			default:
				throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown ruleset for game mode.");
		}
	}

	/// <summary>Recursively counts a Catch hit object and its nested parts into the per-type totals.</summary>
	private static void CountCatchRecursive(HitObject h, ref int fruits, ref int droplets, ref int tinyDroplets,
		ref int bananas)
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

	/// <summary>
	///     A minimal <see cref="WorkingBeatmap" /> for headless difficulty calculation only: no
	///     osu!framework host, audio, or texture access is needed or provided.
	/// </summary>
	private sealed class StreamlessWorkingBeatmap(Beatmap beatmap)
		: WorkingBeatmap(beatmap.BeatmapInfo, null)
	{
		protected override IBeatmap GetBeatmap()
		{
			return beatmap;
		}

		public override Texture? GetBackground()
		{
			return null;
		}

		protected override Track? GetBeatmapTrack()
		{
			return null;
		}

		protected override ISkin? GetSkin()
		{
			return null;
		}

		public override Stream? GetStream(string storagePath)
		{
			return null;
		}
	}
}