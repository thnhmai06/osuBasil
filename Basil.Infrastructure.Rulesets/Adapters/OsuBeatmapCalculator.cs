using Basil.Application.Rulesets.Ports;
using Basil.Domain.Beatmaps;
using Basil.Domain.Game;
using Basil.Domain.Scores;
using Basil.Infrastructure.Rulesets.Storage;
using Basil.Shared.Structures.Cryptography;
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
using RulesetStore = Basil.Infrastructure.Rulesets.Storage.RulesetStore;

namespace Basil.Infrastructure.Rulesets.Adapters;

internal sealed class OsuBeatmapCalculator(
	IBeatmapFiles files,
	AttributeStorage storage,
	RulesetStore rulesets,
	ILogger<OsuBeatmapCalculator> logger) : IBeatmapCalculator
{
	private readonly SingleFlight<(Md5Hash, GameMode, GameMods), Calculated> _flights = new();

	private readonly SemaphoreSlim _gate = new(Environment.ProcessorCount);

	public async Task<double> GetStarsAsync(int beatmapId, Md5Hash checksum, GameMode mode, GameMods mods,
		CancellationToken ct = default)
	{
		return (await CalculatedAsync(beatmapId, checksum, mode, mods, ct)).Attributes.StarRating;
	}

	public async Task<double> GetPerformanceAsync(int beatmapId, Md5Hash checksum, GameMode mode, GameMods mods,
		HitCounts hits, int combo, CancellationToken ct = default)
	{
		var (attributes, snapshot) = await CalculatedAsync(beatmapId, checksum, mode, mods, ct);
		var ruleset = OsuBeatmapAnalyser.CreateRuleset(mode);
		var calculator = ruleset.CreatePerformanceCalculator() ??
		                 throw new InvalidOperationException("The ruleset calculates no pp.");

		var pp = calculator.Calculate(ScoreOf(snapshot, ruleset, mode, PlayMods(ruleset, mode, mods), hits, combo),
			attributes).Total;

		// A non-finite number is a calculator failure, not a pp.
		return double.IsFinite(pp) ? Math.Max(pp, 0) : throw new InvalidOperationException("The calculation gave no usable pp.");
	}

	private async Task<Calculated> CalculatedAsync(int beatmapId, Md5Hash checksum, GameMode mode, GameMods mods,
		CancellationToken ct)
	{
		var mask = mods.ReduceToDifficulty(mode);
		return await ReadAsync(checksum, mode, mask, ct) ??
		       await _flights.RunAsync((checksum, mode, mask), () => ProduceAsync(beatmapId, checksum, mode, mask),
			       ct);
	}

	private string VersionOf(GameMode mode)
	{
		var ruleset = OsuBeatmapAnalyser.CreateRuleset(mode);
		var empty = new LazerBeatmap { BeatmapInfo = { Ruleset = ruleset.RulesetInfo } };
		var version = ruleset.CreateDifficultyCalculator(new StreamlessWorkingBeatmap(empty)).Version;
		return $"{rulesets.Active}/{version}";
	}

	private async Task<Calculated?> ReadAsync(Md5Hash checksum, GameMode mode, GameMods mask, CancellationToken ct)
	{
		var text = await storage.ReadAsync(checksum, mode, mask, ct);
		if (text is null) return null;

		var attributes = NewAttributes(mode);
		return AttributeFile.TryRead(text, VersionOf(mode), attributes) is { } snapshot
			? new Calculated(attributes, snapshot)
			: null;
	}

	private async Task<Calculated> ProduceAsync(int beatmapId, Md5Hash checksum, GameMode mode, GameMods mask)
	{
		// Another run may have written the file between the caller's check and this one's start.
		if (await ReadAsync(checksum, mode, mask, CancellationToken.None) is { } stored) return stored;

		Calculated calculated;
		await _gate.WaitAsync();
		try
		{
			var content = await files.OpenAsync(beatmapId, checksum) ??
			              throw new InvalidOperationException("The server has no file for the beatmap.");
			await using (content)
			{
				try
				{
					var ruleset = OsuBeatmapAnalyser.CreateRuleset(mode);
					var working = new StreamlessWorkingBeatmap(OsuBeatmapAnalyser.Decode(content));
					var legacyMods = ruleset.ConvertFromLegacyMods((LegacyMods)mask).ToArray();
					var attributes = ruleset.CreateDifficultyCalculator(working).Calculate(legacyMods);
					calculated = new Calculated(attributes, SnapshotOf(working, ruleset, mode));
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
			await storage.WriteAsync(checksum, mode, mask,
				AttributeFile.Write(VersionOf(mode), calculated.Attributes, calculated.Snapshot));
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			// The result is still right; the next call calculates it again.
			logger.LogWarning(e, "Could not store the attributes of beatmap {BeatmapId}", beatmapId);
		}

		return calculated;
	}

	private static BeatmapSnapshot SnapshotOf(IWorkingBeatmap working, Ruleset ruleset, GameMode mode)
	{
		var playable = working.GetPlayableBeatmap(ruleset.RulesetInfo, []);
		var display = ruleset.GetAdjustedDisplayDifficulty(working.Beatmap.BeatmapInfo, []);
		var counts = OsuBeatmapAnalyser.BuildObjectCounts(mode, playable.HitObjects, playable.GetMaxCombo());
		var osu = counts as OsuObjectCount;
		return new BeatmapSnapshot(counts.MaxCombo, display.ApproachRate, display.CircleSize, display.DrainRate,
			display.OverallDifficulty, osu?.Circles ?? 0, osu?.Sliders ?? 0, osu?.Spinners ?? 0,
			playable.CalculatePlayableLength() / 1000.0);
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

	private sealed record Calculated(DifficultyAttributes Attributes, BeatmapSnapshot Snapshot);

	private static Mod[] PlayMods(Ruleset ruleset, GameMode mode, GameMods mods)
	{
		var lazer = ruleset.ConvertFromLegacyMods((LegacyMods)mods.RemoveInvalidMods(mode));
		return (mode == GameMode.Standard ? lazer.Append(new OsuModClassic()) : lazer).ToArray();
	}

	private static ScoreInfo ScoreOf(BeatmapSnapshot snapshot, Ruleset ruleset, GameMode mode, Mod[] mods,
		HitCounts hits, int combo)
	{
		var info = new BeatmapInfo(ruleset.RulesetInfo, new LazerDifficulty
		{
			CircleSize = (float)snapshot.Cs,
			ApproachRate = (float)snapshot.Ar,
			OverallDifficulty = (float)snapshot.Od,
			DrainRate = (float)snapshot.Hp
#pragma warning disable CS0618 // the score is built here, with no score manager to ask
		}, new LazerMetadata()) { MaxCombo = snapshot.MaxCombo };
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
}