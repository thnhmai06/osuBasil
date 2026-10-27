using Basil.Application.Beatmaps.Persistence;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps.Capabilities;

/// <summary>One archive read for indexing: the stored beatmapset it belongs to and its records.</summary>
/// <param name="Archive">The archive, which becomes the beatmapset's own.</param>
/// <param name="Existing">The stored beatmapset it belongs to, or <see langword="null" /> when it is a new one.</param>
/// <param name="Data">The beatmapset as read from the archive.</param>
/// <param name="Locked">Whether storing it would change something locked, so it is not stored.</param>
internal sealed record BeatmapsetPlan(
	BeatmapsetArchive Archive,
	BeatmapsetRecord? Existing,
	BeatmapsetData Data,
	bool Locked)
{
	/// <summary>Gets the change that stores the beatmapset.</summary>
	public BeatmapsetChange Change => new(Existing, Data);
}