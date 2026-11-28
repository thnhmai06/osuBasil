using Basil.Domain.Beatmaps;
using Basil.Shared.Structures.Cryptography;
using Mediator;

namespace Basil.Application.Beatmaps.Messages;

public sealed record FindBeatmapByHash(Md5Hash Hash) : IRequest<BeatmapRecord?>;

public sealed record FindBeatmapByFileName(string FileName) : IRequest<BeatmapRecord?>;

/// <summary>Finds the beatmap of each .osu file name; the answer has one entry per name, in order, <see langword="null"/> where none matches.</summary>
public sealed record FindBeatmapsByFileNames(IReadOnlyList<string> FileNames) : IRequest<IReadOnlyList<BeatmapRecord?>>;