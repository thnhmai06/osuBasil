using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using DomainBeatmaps = Basil.Domain.Beatmaps;
using osu.Framework.Audio.Track;
using osu.Framework.Extensions;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.Beatmaps.Legacy;
using osu.Game.IO;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Taiko;
using osu.Game.Skinning;
using osu.Game.Utils;
using Beatmap = osu.Game.Beatmaps.Beatmap;
using GameMode = Basil.Domain.Beatmaps.GameMode;

namespace Basil.Infrastructure.Performance;

/// <inheritdoc cref="IOsuCalculator" />
/// <remarks>
///     Uses ppy's own osu!lazer ruleset libraries (the same engine the real client/website run) —
///     see the sibling osu-difficulty-calculator repo for the reference pattern this is based on.
///     (That repo is a batch CLI orchestrator around the same NuGet packages, not a callable server,
///     so we reference the packages directly instead of shelling out to it.)
/// </remarks>
public sealed class PpyOsuCalculator : IOsuCalculator
{
	public BeatmapAnalysis Analyze(string beatmapFilePath, GameMode mode, Mods mods)
	{
		try
		{
			using var stream = File.OpenRead(beatmapFilePath);
			using var reader = new LineBufferedReader(stream);
			var beatmap = Decoder.GetDecoder<Beatmap>(reader).Decode(reader);

			var ruleset = CreateRuleset(mode);
			var workingBeatmap = new StreamlessWorkingBeatmap(beatmap);

			// 'Mode' already captures Relax/Autopilot — ppy's calculator only needs the
			// difficulty-affecting mods (HR, DT, HT, EZ, HD, FL, ...).
			var strippedMods = mods & ~(Mods.Relax | Mods.Autopilot);
			var legacyMods = ruleset.ConvertFromLegacyMods((LegacyMods)strippedMods).ToArray();

			var attributes = ruleset.CreateDifficultyCalculator(workingBeatmap).Calculate(legacyMods);
			var playable = workingBeatmap.GetPlayableBeatmap(ruleset.RulesetInfo, legacyMods);

			// Ruleset.GetAdjustedDisplayDifficulty applies HR/EZ's CS/AR/OD/HP multiplier AND DT/HT/NC's
			// rate-based AR/OD hit-window scaling itself, per ruleset — the same call osu!'s own song
			// select uses for its displayed stats. No need to hand-roll either adjustment.
			var displayDifficulty = ruleset.GetAdjustedDisplayDifficulty(beatmap.BeatmapInfo, legacyMods);

			// The raw decoder (Decoder.GetDecoder<Beatmap>().Decode(...), used above) never populates
			// BeatmapInfo.Length/MaxCombo/BPM — those fields only exist once the beatmap has been
			// converted for a specific ruleset and its hit objects' timing resolved via ApplyDefaults
			// (which GetPlayableBeatmap does), and even then they live on BeatmapExtensions'
			// computed-on-demand helpers, not back on BeatmapInfo itself. Confirmed by direct
			// inspection: BeatmapInfo.Length/MaxCombo/BPM are 0/null/0 both before AND after
			// GetPlayableBeatmap; CalculatePlayableLength/GetMaxCombo/GetMostCommonBeatLength are the
			// only source of real values, and neither reflects DT/HT/NC's rate change on its own, so
			// Bpm/TotalLength are scaled by hand using the same rate ModUtils.CalculateRateWithMods
			// reports.
			var maxCombo = playable.GetMaxCombo();
			var mostCommonBeatLength = playable.GetMostCommonBeatLength();
			var rate = ModUtils.CalculateRateWithMods(legacyMods);
			var bpm = mostCommonBeatLength > 0 ? 60000 / mostCommonBeatLength * rate : 0;
			var totalLength = TimeSpan.FromMilliseconds(playable.CalculatePlayableLength() / rate);

			var difficulty = new DomainBeatmaps.Difficulty(
				mode, Round(bpm, 1), totalLength,
				Round(displayDifficulty.CircleSize, 1), Round(displayDifficulty.ApproachRate, 1),
				Round(displayDifficulty.OverallDifficulty, 1), Round(displayDifficulty.DrainRate, 1),
				Round(attributes.StarRating, 2));

			var objectCounts = BuildObjectCounts(mode, playable.HitObjects, maxCombo);

			return new BeatmapAnalysis(difficulty, objectCounts);
		}
		catch (Exception e)
		{
			throw new InvalidOperationException(
				$"Failed to analyze beatmap '{beatmapFilePath}'.", e);
		}
	}

	public string ComputeBeatmapMd5(byte[] beatmapBytes)
	{
		using var stream = new MemoryStream(beatmapBytes);
		return stream.ComputeMD5Hash();
	}

	// Raw computed values can carry long floating-point tails (e.g. 5.00000001, or 7.7999997 from
	// HR's 6 * 1.3) — round once here, at the single place every Difficulty gets built. Sr gets an
	// extra decimal of precision (2 vs 1) since star rating differences below 0.1 are still
	// meaningful for map selection.
	private static double Round(double value, int digits)
	{
		return Math.Round(value, digits, MidpointRounding.AwayFromZero);
	}

	/// <summary>
	///     Counts hit objects by concrete type into the per-mode <see cref="BeatmapObjectCounts" />
	///     subtype. Standard/Taiko/Mania count <paramref name="hitObjects" /> at the top level only —
	///     confirmed by direct inspection that <c>playable.HitObjects</c> already excludes nested slider
	///     parts for osu!std, and Taiko/Mania have no equivalent container objects. Catch is the
	///     exception: <c>JuiceStream</c>/<c>BananaShower</c> are containers whose Droplet/TinyDroplet/
	///     Banana children only exist in <see cref="HitObject.NestedHitObjects" />, so counting must
	///     recurse (confirmed empirically — top-level counting only yields Fruit/JuiceStream/
	///     BananaShower, never the droplet/banana breakdown the API needs). <see cref="CatchBeatmapObjectCounts.TinyDroplets" />
	///     is checked before <see cref="CatchBeatmapObjectCounts.Droplets" /> since
	///     <c>TinyDroplet</c> derives from <c>Droplet</c>.
	/// </summary>
	private static DomainBeatmaps.BeatmapObjectCounts BuildObjectCounts(GameMode mode, IReadOnlyList<HitObject> hitObjects,
		int maxCombo)
	{
		switch (mode)
		{
			case GameMode.Standard:
			{
				int circles = 0, sliders = 0, spinners = 0;
				foreach (var h in hitObjects)
					switch (h)
					{
						case osu.Game.Rulesets.Osu.Objects.HitCircle: circles++; break;
						case osu.Game.Rulesets.Osu.Objects.Slider: sliders++; break;
						case osu.Game.Rulesets.Osu.Objects.Spinner: spinners++; break;
					}

				return new DomainBeatmaps.OsuBeatmapObjectCounts
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
						case osu.Game.Rulesets.Taiko.Objects.DrumRoll: drumRolls++; break;
						case osu.Game.Rulesets.Taiko.Objects.Swell: dendens++; break;
						case osu.Game.Rulesets.Taiko.Objects.Hit: hits++; break;
					}

				return new DomainBeatmaps.TaikoBeatmapObjectCounts
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

				return new DomainBeatmaps.CatchBeatmapObjectCounts
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
						case osu.Game.Rulesets.Mania.Objects.HoldNote: holdNotes++; break;
						case osu.Game.Rulesets.Mania.Objects.Note: notes++; break;
					}

				return new DomainBeatmaps.ManiaBeatmapObjectCounts
				{
					Total = notes + holdNotes, MaxCombo = maxCombo, Notes = notes, HoldNotes = holdNotes
				};
			}
			default:
				throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown ruleset for game mode.");
		}
	}

	private static void CountCatchRecursive(HitObject h, ref int fruits, ref int droplets, ref int tinyDroplets,
		ref int bananas)
	{
		switch (h)
		{
			case osu.Game.Rulesets.Catch.Objects.TinyDroplet: tinyDroplets++; break;
			case osu.Game.Rulesets.Catch.Objects.Droplet: droplets++; break;
			case osu.Game.Rulesets.Catch.Objects.Banana: bananas++; break;
			case osu.Game.Rulesets.Catch.Objects.Fruit: fruits++; break;
		}

		foreach (var nested in h.NestedHitObjects)
			CountCatchRecursive(nested, ref fruits, ref droplets, ref tinyDroplets, ref bananas);
	}

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
	///     A minimal <see cref="WorkingBeatmap" /> for headless difficulty calculation only — no
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