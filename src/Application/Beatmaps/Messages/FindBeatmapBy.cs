using Basil.Domain.Beatmaps;
using Basil.Shared.Structures.Cryptography;

using Mediator;

namespace Basil.Application.Beatmaps.Messages;

public sealed record FindBeatmapByHash(Md5Hash Hash) : IRequest<BeatmapRecord?>;

public sealed record FindBeatmapByFileName(string FileName) : IRequest<BeatmapRecord?>;