using Basil.Application.Abstractions.Beatmaps;
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
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Taiko;
using osu.Game.Skinning;
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

			// GetPlayableBeatmap(ruleset, legacyMods) already applies HR/EZ's CS/AR/OD/HP multiplier
			// itself — confirmed by direct inspection: playable.Difficulty reflects HR's 1.3x/1.4x and
			// EZ's 0.5x, matching the documented osu! formulas exactly. Read CS/AR/OD/HP straight off
			// it instead of the raw decode's unmodified BeatmapInfo.Difficulty.
			var playable = workingBeatmap.GetPlayableBeatmap(ruleset.RulesetInfo, legacyMods);
			var objectCounts = new Dictionary<string, int>();
			foreach (var statistic in playable.GetStatistics())
				if (int.TryParse(statistic.Content, out var count))
					objectCounts[statistic.Name.ToString()] = count;

			// The raw decoder (Decoder.GetDecoder<Beatmap>().Decode(...), used above) never populates
			// BeatmapInfo.Length/MaxCombo/BPM — those fields only exist once the beatmap has been
			// converted for a specific ruleset and its hit objects' timing resolved via ApplyDefaults
			// (which GetPlayableBeatmap does), and even then they live on BeatmapExtensions'
			// computed-on-demand helpers, not back on BeatmapInfo itself. Confirmed by direct
			// inspection: BeatmapInfo.Length/MaxCombo/BPM are 0/null/0 both before AND after
			// GetPlayableBeatmap; CalculatePlayableLength/GetMaxCombo/GetMostCommonBeatLength are the
			// only source of real values — and, separately confirmed, neither of those two nor
			// playable.Difficulty.ApproachRate/OverallDifficulty reflect DT/HT/NC's rate change at all
			// (identical to NoMod both with and without GetPlayableBeatmap involved), so rate scaling
			// for Bpm/TotalLength/AR/OD is applied by hand below via DifficultyModCalculator.
			var maxCombo = playable.GetMaxCombo();
			var mostCommonBeatLength = playable.GetMostCommonBeatLength();
			var rate = DomainBeatmaps.DifficultyModCalculator.RateMultiplier(strippedMods);
			var bpm = mostCommonBeatLength > 0 ? 60000 / mostCommonBeatLength * rate : 0;
			var totalLength = TimeSpan.FromMilliseconds(playable.CalculatePlayableLength() / rate);

			var ar = playable.Difficulty.ApproachRate;
			var od = playable.Difficulty.OverallDifficulty;
			if (mode == GameMode.Standard)
			{
				ar = (float)DomainBeatmaps.DifficultyModCalculator.AdjustApproachRateForRate(ar, rate);
				od = (float)DomainBeatmaps.DifficultyModCalculator.AdjustOverallDifficultyForRate(od, rate);
			}

			var difficulty = new DomainBeatmaps.Difficulty(
				mode, Round(bpm, 1), totalLength,
				Round(playable.Difficulty.CircleSize, 1), Round(ar, 1), Round(od, 1),
				Round(playable.Difficulty.DrainRate, 1), Round(attributes.StarRating, 2));

			return new BeatmapAnalysis(difficulty, objectCounts, maxCombo);
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