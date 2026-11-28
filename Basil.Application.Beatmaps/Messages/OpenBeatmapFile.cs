using Basil.Application.Beatmaps.Storage;
using Basil.Application.Shared.Actors;
using Basil.Application.Shared.Storage;
using Mediator;

namespace Basil.Application.Beatmaps.Messages;

public sealed record OpenBeatmapFile(IActor? Actor, int BeatmapId, BeatmapsetFileType Type) : IRequest<BeatmapFile?>;

public sealed record OpenBeatmapsetFile(IActor? Actor, int SetId, BeatmapsetFileType Type) : IRequest<BeatmapFile?>;

public sealed record OpenBeatmapsetArchive(IActor? Actor, int SetId, bool WithVideo) : IRequest<Stream?>;

public sealed record OpenBeatmapsetCover(int SetId, BeatmapsetCoverVariant Variant) : IRequest<ImageFile?>;

public sealed record BeatmapFile(Stream Content, string Name);