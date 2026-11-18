using Basil.Application.Beatmaps.Ports;
using Basil.Application.Beatmaps.Storage;
using Basil.Domain.Beatmaps;
using Basil.Domain.Cryptography;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Infrastructure.Beatmaps.Storage;
using Microsoft.Extensions.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Catch.Difficulty;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mania.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Taiko.Difficulty;
using osu.Game.Scoring;
using LazerDifficulty = osu.Game.Beatmaps.BeatmapDifficulty;
using LazerMetadata = osu.Game.Beatmaps.BeatmapMetadata;
using LazerBeatmap = osu.Game.Beatmaps.Beatmap;

namespace Basil.Infrastructure.Beatmaps.Adapters;

internal sealed class OsuBeatmapCalculator(
	FileBeatmapsetStorage storage,
	ILogger<OsuBeatmapCalculator> logger) : IBeatmapCalculator
{
	private readonly SingleFlight<(Md5, GameMode, GameMods), DifficultyAttributes> _flights =
		new();

	private readonly SemaphoreSlim _gate = new(Environment.ProcessorCount);

	public async Task<double> GetStarsAsync(BeatmapRecord beatmap, GameMode mode, GameMods mods,
		CancellationToken ct = default)
	{
		return (await AttributesAsync(beatmap, mode, mods, ct)).StarRating;
	}

	public async Task<BeatmapPerformance> GetPerformanceAsync(BeatmapRecord beatmap, GameMode mode, GameMods mods,
		HitCounts hits, int combo, CancellationToken ct = default)
	{
		var attributes = await AttributesAsync(beatmap, mode, mods, ct);
		var ruleset = OsuBeatmapAnalyser.CreateRuleset(mode);
		var calculator = ruleset.CreatePerformanceCalculator() ??
		                 throw new InvalidOperationException("The ruleset calculates no pp.");

		var lazerMods = PlayMods(ruleset, mode, mods);
		var pp = calculator.Calculate(ScoreOf(beatmap, ruleset, mode, lazerMods, hits, combo), attributes).Total;
		var maxPp = calculator
			.Calculate(ScoreOf(beatmap, ruleset, mode, lazerMods, PerfectHits(beatmap), attributes.MaxCombo), attributes)
			.Total;

		// A non-finite number is a calculator failure, not a pp.
		if (!double.IsFinite(pp) || !double.IsFinite(maxPp))
			throw new InvalidOperationException("The calculation gave no usable pp.");

		pp = Math.Max(pp, 0);
		return new BeatmapPerformance(pp, Math.Max(maxPp, pp));
	}

	private async Task<DifficultyAttributes> AttributesAsync(BeatmapRecord beatmap, GameMode mode, GameMods mods,
		CancellationToken ct)
	{
		var mask = mods.ReduceToDifficulty(mode);
		return await ReadAsync(beatmap, mode, mask, ct) ??
		       await _flights.RunAsync((beatmap.Hash, mode, mask), () => ProduceAsync(beatmap, mode, mask), ct);
	}

	private async Task<DifficultyAttributes?> ReadAsync(BeatmapRecord beatmap, GameMode mode, GameMods mask,
		CancellationToken ct)
	{
		var text = await storage.ReadAttributesAsync(beatmap.Beatmapset, beatmap.Hash, mode, mask, ct);
		if (text is null) return null;

		var attributes = NewAttributes(mode);
		return AttributeFile.TryRead(text, VersionOf(mode), attributes, new StoredOnlineInfo(beatmap))
			? attributes
			: null;
	}

	private async Task<DifficultyAttributes> ProduceAsync(BeatmapRecord beatmap, GameMode mode, GameMods mask)
	{
		// Another run may have written the file between the caller's check and this one's start.
		if (await ReadAsync(beatmap, mode, mask, CancellationToken.None) is { } stored) return stored;

		DifficultyAttributes attributes;
		await _gate.WaitAsync();
		try
		{
			var content = await storage.OpenAsync(beatmap.Beatmapset, BeatmapsetFileType.Version,
				                  $"{beatmap.Hash}.osu") ??
			              throw new InvalidOperationException("The server has no file for the beatmap.");
			await using (content)
			{
				try
				{
					var ruleset = OsuBeatmapAnalyser.CreateRuleset(mode);
					var working = new StreamlessWorkingBeatmap(OsuBeatmapAnalyser.Decode(content));
					attributes = ruleset.CreateDifficultyCalculator(working)
						.Calculate(ruleset.ConvertFromLegacyMods((LegacyMods)mask).ToArray());
				}
				catch (Exception e) when (e is not OperationCanceledException)
				{
					throw new InvalidOperationException("The beatmap file could not be calculated.", e);
				}
			}
		}
		finally
		{
			_gate.Release();
		}

		try
		{
			await storage.WriteAttributesAsync(beatmap.Beatmapset, beatmap.Hash, mode, mask,
				AttributeFile.Write(VersionOf(mode), attributes));
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			// The result is still right; the next call calculates it again.
			logger.LogWarning(e, "Could not store the attributes of beatmap {BeatmapId}", beatmap.Id);
		}

		return attributes;
	}

	private static int VersionOf(GameMode mode)
	{
		var ruleset = OsuBeatmapAnalyser.CreateRuleset(mode);
		var empty = new LazerBeatmap { BeatmapInfo = { Ruleset = ruleset.RulesetInfo } };
		return ruleset.CreateDifficultyCalculator(new StreamlessWorkingBeatmap(empty)).Version;
	}

	private static DifficultyAttributes NewAttributes(GameMode mode)
	{
		return mode switch
		{
			GameMode.Standard => new OsuDifficultyAttributes(),
			GameMode.Taiko => new TaikoDifficultyAttributes(),
			GameMode.Catch => new CatchDifficultyAttributes(),
			GameMode.Mania => new ManiaDifficultyAttributes(),
			_ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown ruleset for game mode.")
		};
	}

	private static Mod[] PlayMods(Ruleset ruleset, GameMode mode, GameMods mods)
	{
		var lazer = ruleset.ConvertFromLegacyMods((LegacyMods)mods.RemoveInvalidMods(mode));
		return (mode == GameMode.Standard ? lazer.Append(new OsuModClassic()) : lazer).ToArray();
	}

	private static ScoreInfo ScoreOf(BeatmapRecord beatmap, Ruleset ruleset, GameMode mode, Mod[] mods,
		HitCounts hits, int combo)
	{
		var difficulty = beatmap.Metadata.Difficulty;
		var info = new BeatmapInfo(ruleset.RulesetInfo, new LazerDifficulty
		{
			CircleSize = (float)difficulty.Cs,
			ApproachRate = (float)difficulty.Ar,
			OverallDifficulty = (float)difficulty.Od,
			DrainRate = (float)difficulty.Hp
#pragma warning disable CS0618 // the score is built here, with no score manager to ask
		}, new LazerMetadata()) { MaxCombo = beatmap.Metadata.ObjectCount.MaxCombo };
#pragma warning restore CS0618

		return new ScoreInfo(info, ruleset.RulesetInfo)
		{
			Mods = mods,
			MaxCombo = combo,
			Accuracy = AccuracyOf(mode, hits),
			Statistics = StatisticsOf(mode, hits)
		};
	}

	private static double AccuracyOf(GameMode mode, HitCounts hits)
	{
		double earned, total;
		switch (mode)
		{
			case GameMode.Standard:
				earned = 300.0 * hits.Num300 + 100.0 * hits.Num100 + 50.0 * hits.Num50;
				total = 300.0 * (hits.Num300 + hits.Num100 + hits.Num50 + hits.NumMiss);
				break;
			case GameMode.Taiko:
				earned = hits.Num300 + 0.5 * hits.Num100;
				total = hits.Num300 + hits.Num100 + hits.NumMiss;
				break;
			case GameMode.Catch:
				earned = hits.Num300 + hits.Num100 + hits.Num50;
				total = earned + hits.NumKatu + hits.NumMiss;
				break;
			default:
				earned = 300.0 * (hits.NumGeki + hits.Num300) + 200.0 * hits.NumKatu + 100.0 * hits.Num100 +
				         50.0 * hits.Num50;
				total = 300.0 * (hits.NumGeki + hits.Num300 + hits.NumKatu + hits.Num100 + hits.Num50 + hits.NumMiss);
				break;
		}

		return total > 0 ? earned / total : 1;
	}

	private static Dictionary<HitResult, int> StatisticsOf(GameMode mode, HitCounts hits)
	{
		return mode switch
		{
			GameMode.Standard => new Dictionary<HitResult, int>
			{
				[HitResult.Great] = hits.Num300, [HitResult.Ok] = hits.Num100, [HitResult.Meh] = hits.Num50,
				[HitResult.Miss] = hits.NumMiss
			},
			GameMode.Taiko => new Dictionary<HitResult, int>
			{
				[HitResult.Great] = hits.Num300, [HitResult.Ok] = hits.Num100, [HitResult.Miss] = hits.NumMiss
			},
			GameMode.Catch => new Dictionary<HitResult, int>
			{
				[HitResult.Great] = hits.Num300, [HitResult.LargeTickHit] = hits.Num100,
				[HitResult.SmallTickHit] = hits.Num50, [HitResult.SmallTickMiss] = hits.NumKatu,
				[HitResult.Miss] = hits.NumMiss
			},
			_ => new Dictionary<HitResult, int>
			{
				[HitResult.Perfect] = hits.NumGeki, [HitResult.Great] = hits.Num300, [HitResult.Good] = hits.NumKatu,
				[HitResult.Ok] = hits.Num100, [HitResult.Meh] = hits.Num50, [HitResult.Miss] = hits.NumMiss
			}
		};
	}

	private static HitCounts PerfectHits(BeatmapRecord beatmap)
	{
		return beatmap.Metadata.ObjectCount switch
		{
			OsuObjectCount osu => new HitCounts(osu.Total, 0, 0, 0, 0, 0),
			TaikoObjectCount taiko => new HitCounts(taiko.Hits, 0, 0, 0, 0, 0),
			CatchObjectCount ctb => new HitCounts(ctb.Fruits, ctb.Droplets, ctb.TinyDroplets, 0, 0, 0),
			ManiaObjectCount mania => new HitCounts(0, 0, 0, mania.Total, 0, 0),
			_ => new HitCounts(beatmap.Metadata.ObjectCount.Total, 0, 0, 0, 0, 0)
		};
	}
}
