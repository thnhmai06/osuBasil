using Basil.Domain.Beatmaps;

namespace Basil.Application.Abstractions.Beatmaps;

/// <summary>
///     Repository for the Mapsets table — bancho.py has no equivalent (a beatmap set there is just a
///     grouping of `maps` rows fetched from osu!api, never a row of its own). Here it holds the
///     set-level fields shared by every difficulty; BeatmapIngestionService/BeatmapWatcherService
///     are the only writers.
/// </summary>
public interface IMapsetRepository
{
    Task<Mapset?> FetchByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Mapset> UpsertAsync(Mapset mapset, CancellationToken cancellationToken = default);

    /// <summary>Cascades to every Beatmaps row referencing this set (FK on delete cascade).</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>The highest Id currently in use (0 if the table is empty), for local-id allocation.</summary>
    Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default);

    /// <summary>Every Mapset id in the DB — used by the full reconciliation pass to find rows whose backing folder no longer exists on disk.</summary>
    Task<IReadOnlyList<int>> FetchAllIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Newest-first page of mapsets, for the `api.` host's `GET /beatmapsets` list. When
    ///     <paramref name="onlyWithVisibleBeatmaps" /> is true, a mapset whose <see cref="Mapset.IsPrivate" />
    ///     flag is set is excluded entirely (the public, non-admin view) — pass false for an
    ///     admin-elevated caller, who sees every mapset regardless.
    /// </summary>
    Task<IReadOnlyList<Mapset>> FetchPageAsync(int offset, int limit, bool onlyWithVisibleBeatmaps,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Toggles the write-lock the `api.` host's `PATCH /beatmapsets/{id}` route sets — blocks
    ///     `PUT`/`DELETE /beatmapsets/{id}` (409) regardless of admin role until unfrozen again.
    /// </summary>
    Task SetFrozenAsync(int id, bool frozen, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Toggles <see cref="Mapset.IsPrivate" /> — the set-level visibility flag hiding every
    ///     beatmap under this mapset from non-admin listings/lookups. Set via `PATCH /beatmapsets/{id}`.
    /// </summary>
    Task SetPrivateAsync(int id, bool isPrivate, CancellationToken cancellationToken = default);
}
