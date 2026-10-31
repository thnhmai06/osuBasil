namespace Basil.Protocol.Bancho.Models.Beatmaps;

/// <summary>Carries the beatmaps a client asks about in the beatmap-info-request packet.</summary>
/// <param name="Filenames">The beatmap file names the client asks about.</param>
/// <param name="Ids">The beatmap ids the client asks about.</param>
public sealed record BeatmapInfoRequest(IReadOnlyList<string> Filenames, IReadOnlyList<int> Ids);