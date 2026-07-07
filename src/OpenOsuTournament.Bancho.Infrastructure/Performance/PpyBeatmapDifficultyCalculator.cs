using OpenOsuTournament.Bancho.Application.Abstractions.Beatmaps;
using OpenOsuTournament.Bancho.Domain;
using OpenOsuTournament.Bancho.Domain.Beatmaps;
using osu.Framework.Audio.Track;
using osu.Framework.Extensions.IEnumerableExtensions;
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
using GameMode = OpenOsuTournament.Bancho.Domain.Beatmaps.GameMode;

namespace OpenOsuTournament.Bancho.Infrastructure.Performance;

/// <inheritdoc cref="IBeatmapDifficultyCalculator" />
/// <remarks>
///     Uses ppy's own osu!lazer ruleset libraries (the same engine the real client/website run) —
///     see V:\Code\cs\OpenOsuTournament\osu-difficulty-calculator for the reference pattern this is
///     based on (that repo is a batch CLI orchestrator around the same NuGet packages, not a callable
///     server, so we reference the packages directly instead of shelling out to it).
/// </remarks>
public sealed class PpyBeatmapDifficultyCalculator : IBeatmapDifficultyCalculator
{
    public double CalculateStarRating(string beatmapFilePath, GameMode mode, Mods mods)
    {
        try
        {
            using var stream = File.OpenRead(beatmapFilePath);
            using var reader = new LineBufferedReader(stream);
            var beatmap = Decoder.GetDecoder<osu.Game.Beatmaps.Beatmap>(reader).Decode(reader);

            var ruleset = CreateRuleset(mode);
            var workingBeatmap = new StreamlessWorkingBeatmap(beatmap);

            // Relax/Autopilot are already captured by `mode` — ppy's calculator only needs the
            // difficulty-affecting mods (HR, DT, HT, EZ, HD, FL, ...).
            var strippedMods = mods & ~(Mods.Relax | Mods.Autopilot);
            var legacyMods = ruleset.ConvertFromLegacyMods((LegacyMods)strippedMods).ToArray();

            var attributes = ruleset.CreateDifficultyCalculator(workingBeatmap).Calculate(legacyMods);
            return attributes.StarRating;
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(
                $"Failed to calculate star rating for beatmap '{beatmapFilePath}'.", e);
        }
    }

    private static Ruleset CreateRuleset(GameMode mode) => mode.AsVanilla() switch
    {
        0 => new OsuRuleset(),
        1 => new TaikoRuleset(),
        2 => new CatchRuleset(),
        3 => new ManiaRuleset(),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown ruleset for game mode.")
    };

    /// <summary>
    ///     A minimal <see cref="WorkingBeatmap" /> for headless difficulty calculation only — no
    ///     osu!framework host, audio, or texture access is needed or provided.
    /// </summary>
    private sealed class StreamlessWorkingBeatmap : WorkingBeatmap
    {
        private readonly osu.Game.Beatmaps.Beatmap beatmap;

        public StreamlessWorkingBeatmap(osu.Game.Beatmaps.Beatmap beatmap)
            : base(beatmap.BeatmapInfo, null)
        {
            this.beatmap = beatmap;
        }

        protected override IBeatmap GetBeatmap() => beatmap;
        public override Texture? GetBackground() => null;
        protected override Track? GetBeatmapTrack() => null;
        protected override ISkin? GetSkin() => null;
        public override Stream? GetStream(string storagePath) => null;
    }
}
