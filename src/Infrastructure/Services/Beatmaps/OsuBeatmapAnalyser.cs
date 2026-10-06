using Basil.Application.Services.Contracts.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.Beatmaps.Legacy;
using osu.Game.IO;
using osu.Game.Rulesets;
using osu.Game.Skinning;
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
using Beatmap = osu.Game.Beatmaps.Beatmap;

namespace Basil.Infrastructure.Services.Beatmaps;

/// <summary>
///     Computes beatmap gameplay stats with ppy's osu!lazer ruleset libraries, the same engine the
///     real client and website run.
/// </summary>
/// <remarks>
///     The server uses these numbers for star rating and difficulty display only; they feed no
///     scoring, leaderboard, or match win condition.
/// </remarks>
internal sealed class OsuBeatmapAnalyser : IBeatmapAnalyser
{
	/// <inheritdoc />
	/// <exception cref="Exception">The beatmap file could not be decoded or analyzed.</exception>
	public BeatmapAnalysis Analyze(byte[] content, GameMode mode, GameMods mods)
	{
		using var stream = new MemoryStream(content);
		using var reader = new LineBufferedReader(stream, true);
		var beatmap = Decoder.GetDecoder<Beatmap>(reader).Decode(reader);

		var ruleset = CreateRuleset(mode);
		var workingBeatmap = new StreamlessWorkingBeatmap(beatmap);

		var legacyMods = ruleset.ConvertFromLegacyMods((LegacyMods)mods).ToArray();
		var attributes = ruleset.CreateDifficultyCalculator(workingBeatmap).Calculate(legacyMods);
		var playable = workingBeatmap.GetPlayableBeatmap(ruleset.RulesetInfo, legacyMods);

		var displayDifficulty = ruleset.GetAdjustedDisplayDifficulty(beatmap.BeatmapInfo, legacyMods);
		var maxCombo = playable.GetMaxCombo();
		var mostCommonBeatLength = playable.GetMostCommonBeatLength();
		var rate = ModUtils.CalculateRateWithMods(legacyMods);
		var bpm = mostCommonBeatLength > 0 ? 60000 / mostCommonBeatLength * rate : 0;
		var totalLength = TimeSpan.FromMilliseconds(playable.CalculatePlayableLength() / rate);

		// The domain difficulty requires each setting to stay within [0, 10], but a mod such as
		// HardRock can push the adjusted value past that; clamp so the result stays valid.
		var difficulty = new Difficulty(
			mode, Round(bpm, 1), totalLength,
			Clamp(displayDifficulty.CircleSize), Clamp(displayDifficulty.ApproachRate),
			Clamp(displayDifficulty.OverallDifficulty), Clamp(displayDifficulty.DrainRate),
			Round(attributes.StarRating, 2));

		var objects = BuildObjectCounts(mode, playable.HitObjects, maxCombo);

		return new BeatmapAnalysis(difficulty, objects);
	}

	private static double Clamp(double value) => Math.Clamp(value, 0d, 10d);

	private static double Round(double value, int digits) =>
		Math.Round(value, digits, MidpointRounding.AwayFromZero);

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
				foreach (var h in hitObjects)
					switch (h)
					{
						case HitCircle: circles++; break;
						case Slider: sliders++; break;
						case Spinner: spinners++; break;
					}

				var o = (OsuObjects)BeatmapObjects.NewFrom(mode);
				o.Total = circles + sliders + spinners;
				o.MaxCombo = maxCombo;
				o.Circles = circles;
				o.Sliders = sliders;
				o.Spinners = spinners;
				return o;
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

				var o = (TaikoObjects)BeatmapObjects.NewFrom(mode);
				o.Total = hits + drumRolls + dendens;
				o.MaxCombo = maxCombo;
				o.Hits = hits;
				o.DrumRolls = drumRolls;
				o.Dendens = dendens;
				return o;
			}
			case GameMode.Catch:
			{
				int fruits = 0, droplets = 0, tinyDroplets = 0, bananas = 0;
				foreach (var h in hitObjects)
					CountCatchRecursive(h, ref fruits, ref droplets, ref tinyDroplets, ref bananas);

				var o = (CatchObjects)BeatmapObjects.NewFrom(mode);
				o.Total = fruits + droplets + tinyDroplets + bananas;
				o.MaxCombo = maxCombo;
				o.Fruits = fruits;
				o.Droplets = droplets;
				o.TinyDroplets = tinyDroplets;
				o.Bananas = bananas;
				return o;
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

				var o = (ManiaObjects)BeatmapObjects.NewFrom(mode);
				o.Total = notes + holdNotes;
				o.MaxCombo = maxCombo;
				o.Notes = notes;
				o.HoldNotes = holdNotes;
				return o;
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

	/// <summary>
	///     A minimal <see cref="WorkingBeatmap" /> for headless difficulty calculation only: no
	///     osu!framework host, audio, or texture access is needed or provided.
	/// </summary>
	private sealed class StreamlessWorkingBeatmap(Beatmap beatmap)
		: WorkingBeatmap(beatmap.BeatmapInfo, null)
	{
		protected override IBeatmap GetBeatmap() => beatmap;

		public override Texture? GetBackground() => null;

		protected override Track? GetBeatmapTrack() => null;

		protected override ISkin? GetSkin() => null;

		public override Stream? GetStream(string storagePath) => null;
	}
}
