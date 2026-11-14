using Basil.Domain.Beatmaps;
using Basil.Domain.Cryptography;
using Mediator;

namespace Basil.Application.Beatmaps.Messages;

/// <summary>Asks Beatmaps for the beatmap with a given hash.</summary>
/// <param name="Hash">The hash of the beatmap file.</param>
/// <remarks>The answer is <see langword="null" /> when the server knows no such beatmap.</remarks>
public sealed record FindBeatmapByHash(Md5 Hash) : IRequest<BeatmapRecord?>;

/// <summary>Asks Beatmaps for the beatmap whose file has a given name.</summary>
/// <param name="FileName">The file name of the beatmap.</param>
/// <remarks>The answer is <see langword="null" /> when the server knows no such beatmap.</remarks>
public sealed record FindBeatmapByFileName(string FileName) : IRequest<BeatmapRecord?>;