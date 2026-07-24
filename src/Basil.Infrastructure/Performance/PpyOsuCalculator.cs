using Basil.Application.Abstractions.Beatmaps;
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

            var playable = workingBeatmap.GetPlayableBeatmap(ruleset.RulesetInfo, legacyMods);
            var objectCounts = new Dictionary<string, int>();
            foreach (var statistic in playable.GetStatistics())
                if (int.TryParse(statistic.Content, out var count))
                    objectCounts[statistic.Name.ToString()] = count;

            return new BeatmapAnalysis(attributes.StarRating, objectCounts);
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
