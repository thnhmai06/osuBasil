namespace Basil.Domain.Beatmaps;

/// <summary>The understood content of a whole .osu file: its hit objects, timing and difficulty settings.</summary>
/// <remarks>
///     It has no members of its own: only the component that analyses beatmaps reads it. It complements, and never
///     replaces, the beatmap's stored data.
/// </remarks>
public abstract class BeatmapContent;
