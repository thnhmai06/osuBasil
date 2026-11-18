namespace Basil.Protocol.Game.Stable.Models.Beatmaps;

public sealed record BeatmapInfoRequest(IReadOnlyList<string> Filenames, IReadOnlyList<int> Ids);