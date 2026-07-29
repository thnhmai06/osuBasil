namespace Basil.Infrastructure.Tests.Beatmaps;

/// <summary>
///     Serializes the three test classes that spin up real FileSystemWatcher/timer-driven background
///     services against their own temp directories — running them concurrently with each other adds
///     timing noise (debounce windows, GC-pass polling) under load without adding coverage, since
///     none of them share state.
/// </summary>
[CollectionDefinition(Name)]
public sealed class BeatmapFilesystemTestCollection
{
	public const string Name = "Beatmap Filesystem Tests";
}