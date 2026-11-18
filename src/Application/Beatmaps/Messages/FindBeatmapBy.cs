using Basil.Domain.Beatmaps;
using Basil.Domain.Cryptography;
using Mediator;

namespace Basil.Application.Beatmaps.Messages;

public sealed record FindBeatmapByHash(Md5 Hash) : IRequest<BeatmapRecord?>;

public sealed record FindBeatmapByFileName(string FileName) : IRequest<BeatmapRecord?>;