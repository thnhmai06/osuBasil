using Basil.Application.Beatmaps.Storage;
using Basil.Application.Common;
using Mediator;

namespace Basil.Application.Beatmaps.Messages;

/// <summary>Asks Beatmaps to open a file that belongs to a beatmap.</summary>
/// <param name="Actor">The actor that asks, or <see langword="null" /> for a caller without a session.</param>
/// <param name="BeatmapId">The id of the beatmap.</param>
/// <param name="Type">Which file: the audio, the video or the background.</param>
/// <remarks>The answer is <see langword="null" /> when the beatmap does not exist, the actor may not see it or it has no such file.</remarks>
public sealed record OpenBeatmapFile(IActor? Actor, int BeatmapId, BeatmapsetFileType Type) : IRequest<BeatmapFile?>;

/// <summary>Asks Beatmaps to open a file that belongs to the first beatmap of a beatmapset that has it.</summary>
/// <param name="Actor">The actor that asks, or <see langword="null" /> for a caller without a session.</param>
/// <param name="SetId">The id of the beatmapset.</param>
/// <param name="Type">Which file: the audio preview or the storyboard.</param>
/// <remarks>The answer is <see langword="null" /> when the beatmapset does not exist, the actor may not see it or it has no such file.</remarks>
public sealed record OpenBeatmapsetFile(IActor? Actor, int SetId, BeatmapsetFileType Type) : IRequest<BeatmapFile?>;

/// <summary>Asks Beatmaps to open the archive of a beatmapset.</summary>
/// <param name="Actor">The actor that asks, or <see langword="null" /> for a caller without a session.</param>
/// <param name="SetId">The id of the beatmapset.</param>
/// <param name="WithVideo">Whether the archive keeps the video.</param>
/// <remarks>The answer is <see langword="null" /> when the beatmapset does not exist, the actor may not see it or it has no archive.</remarks>
public sealed record OpenBeatmapsetArchive(IActor? Actor, int SetId, bool WithVideo) : IRequest<Stream?>;

/// <summary>Asks Beatmaps to open the cover picture of a visible beatmapset.</summary>
/// <param name="SetId">The id of the beatmapset.</param>
/// <remarks>The picture is public: the answer is <see langword="null" /> for a hidden beatmapset or one without a picture.</remarks>
public sealed record OpenBeatmapsetCover(int SetId) : IRequest<Stream?>;

/// <summary>An opened file of a beatmap or a beatmapset.</summary>
/// <param name="Content">The content, which the receiver disposes.</param>
/// <param name="Name">The file name as stored.</param>
public sealed record BeatmapFile(Stream Content, string Name);