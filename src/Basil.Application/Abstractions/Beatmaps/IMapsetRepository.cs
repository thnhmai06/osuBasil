namespace Basil.Application.Abstractions.Beatmaps;

/// <summary>
///     Repository for the Mapsets table — bancho.py has no equivalent (a beatmap set there is just a
///     grouping of `maps` rows fetched from osu!api, never a row of its own). Here it exists purely
///     so Beatmaps.SetId has something to reference; BeatmapIngestionService is the only writer.
/// </summary>
public interface IMapsetRepository
{
    /// <summary>Ported nowhere — INSERT-if-absent so re-ingesting a set already on disk is a no-op.</summary>
    Task EnsureExistsAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>The highest Id currently in use (0 if the table is empty), for local-id allocation.</summary>
    Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default);
}