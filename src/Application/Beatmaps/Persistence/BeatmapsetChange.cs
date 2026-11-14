using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps.Persistence;

/// <summary>One beatmapset to store.</summary>
/// <param name="Existing">The stored beatmapset it replaces, or <see langword="null" /> to create a new one.</param>
/// <param name="Data">The beatmapset as it is now; stored difficulties it no longer has are removed.</param>
public sealed record BeatmapsetChange(BeatmapsetRecord? Existing, BeatmapsetData Data);

/// <summary>What one scan or import changes, all at once: the beatmapsets to store and those to remove.</summary>
/// <param name="Store">The beatmapsets to create or replace, each with every difficulty it now has.</param>
/// <param name="Remove">The stored beatmapsets to remove with their beatmaps.</param>
public sealed record BeatmapsetChanges(
	IReadOnlyList<BeatmapsetChange> Store,
	IReadOnlyCollection<BeatmapsetRecord> Remove);