# Beatmap ingestion

## Overview

Basil supports two beatmap serving modes, controlled by `Basil:Mirror`:

* **Offline mode**: both `DownloadEndpoint` and `SearchEndpoint` are `null`. Beatmaps are served exclusively from the local `Mapsets/` directory.
* **Online mode**: one or both mirror endpoints are configured. Basil can use an external beatmap mirror for downloads and/or osu!direct search.

Local beatmap ingestion is independent of the mirror configuration. Beatmaps placed in `Mapsets/` are always ingested, analyzed, and indexed by Basil regardless of whether the server is running in offline or online mode.

This distinction is important:

> **The mirror controls where Basil can obtain and discover beatmaps. It does not control whether locally stored beatmaps are ingested.**

A locally ingested beatmap can therefore exist in the database even when the server is using an external mirror.

## Mirror modes

The mirror configuration is under:

```text
Basil:Mirror:DownloadEndpoint
Basil:Mirror:SearchEndpoint
```

### Offline mode

Offline mode is enabled when both endpoints are `null`:

```json
{
  "Basil": {
    "Mirror": {
      "DownloadEndpoint": null,
      "SearchEndpoint": null
    }
  }
}
```

In this mode:

* beatmaps are obtained from the local `Mapsets/` directory;
* locally ingested beatmaps can be served and downloaded;
* osu!direct search/browse does not use an external mirror;
* Basil has no dependency on an external beatmap service.

This is the mode intended for completely isolated networks such as tournament LANs.

### Online mode

Online mode is enabled by configuring one or both mirror endpoints.

The endpoints have separate responsibilities:

* `DownloadEndpoint` provides beatmapset downloads from the external mirror.
* `SearchEndpoint` provides beatmap search/discovery for osu!direct.

They are independent settings, so Basil can support configurations where only one capability is provided.

### Locally ingested beatmaps in online mode

Local ingestion continues to work in online mode.

A beatmapset imported into `Mapsets/` is processed normally:

```text
.osz
  │
  ▼
local ingestion
  │
  ├── extract
  ├── analyze
  └── index in database
```

The resulting beatmap can still be downloaded through osu!direct when the online download path is available.

However, **locally ingested beatmaps are not included in osu!direct search/browse results**.

This is intentional. The mirror search index determines what appears in osu!direct discovery; local ingestion does not automatically publish a beatmap into that external index.

The two concepts are therefore separate:

```text
                    Basil
                      │
          ┌───────────┴───────────┐
          │                       │
    Local ingestion          Mirror integration
          │                       │
          ▼                       ▼
     Mapsets/                 SearchEndpoint
          │                       │
          ▼                       ▼
   local database          osu!direct discovery
          │
          └──────► download
```

A map can therefore be **available locally and downloadable without being discoverable through osu!direct search**.

## Ingestion contract

The ingestion pipeline accepts **beatmapsets**, not individual difficulty files.

* A `.osz` placed in `Mapsets/` is automatically detected, extracted into its own directory, analyzed, and synchronized with the database. No restart or manual action is required.
* A standalone `.osu` file placed directly in `Mapsets/` is ignored. Without the containing beatmapset, Basil cannot reliably determine its ownership and does not attempt to infer one.
* `POST /beatmapsets` on the admin API accepts `.osz` files only and uses the same ingestion pipeline as filesystem ingestion.
* Star rating, BPM, length, and object counts are calculated once during ingestion and persisted with the beatmap metadata.
* Reading a beatmap never triggers difficulty recalculation.
* Ingestion happens regardless of whether Basil is in offline or online mirror mode.

### `ffmpeg` is optional

`ffmpeg` is only required for generating audio previews.

If it is unavailable or preview generation fails:

* the preview endpoint returns `503 Service Unavailable`;
* the failure is logged;
* the rest of beatmap ingestion and serving continues normally.

No other part of Basil depends on `ffmpeg`.

## Source of truth

Each locally ingested beatmapset is represented on disk by its own directory:

```text
Mapsets/
└── {id} Artist - Title/
    ├── audio.mp3
    ├── background.jpg
    ├── video.mp4
    ├── *.osu
    └── *.osb
```

The directory contains the original contents of the `.osz` archive.

The database mirrors this filesystem state:

```text
Beatmapsets
    │
    └── Beatmaps
```

The `Beatmapsets` and `Beatmaps` rows describe the locally ingested beatmap data. They do not replace the source files on disk.

This distinction is important when modifying the ingestion pipeline:

> **For locally ingested beatmaps, the filesystem is authoritative and the database is its indexed representation.**

Mirror-hosted beatmaps are a separate concern. They do not need to exist in `Mapsets/` merely to be discoverable or downloadable through the configured mirror.

## Ingestion triggers

Basil has two mechanisms for keeping the local beatmap database synchronized with `Mapsets/`.

### Filesystem watcher

`[BeatmapWatcherService](../../src/Basil.Infrastructure/Beatmaps/BeatmapWatcherService.cs)` monitors the directory for changes.

When a new `.osz` appears, the watcher schedules it for ingestion. Under normal conditions, the change becomes visible within a few seconds.

### Startup reconciliation

The watcher cannot account for changes that happen while Basil is offline.

Therefore, Basil performs a full reconciliation during startup. This discovers beatmapsets that were added, removed, or otherwise changed while the server was not running.

Both mechanisms eventually invoke the same ingestion service. There is no separate implementation for startup discovery, filesystem changes, or admin uploads.

## Ingestion lifecycle

```text
.osz
 │
 ├── Mapsets/ filesystem
 │       │
 │       └── BeatmapWatcherService
 │
 └── POST /beatmapsets
         │
         ▼
BeatmapIngestionService
         │
         ├── extract archive
         │
         ├── analyze beatmaps
         │      ├── star rating
         │      ├── BPM
         │      ├── length
         │      └── object counts
         │
         └── synchronize database
                 │
                 ├── Beatmapsets
                 └── Beatmaps
                         │
                         ▼
                  local beatmap data
```

Startup reconciliation enters the same pipeline:

```text
server startup
      │
      ▼
filesystem reconciliation
      │
      ▼
BeatmapIngestionService
```

Mirror search/download does **not** replace this pipeline. It is an independent source of remote beatmap discovery and acquisition.

## Difficulty calculation

Basil calculates beatmap difficulty metadata during ingestion using osu!'s ruleset calculation libraries.

The calculation is intentionally limited to information needed for presentation and indexing, such as star rating.

Basil does **not** calculate pp.

These values have no effect on:

* score validation;
* leaderboards;
* multiplayer results;
* match outcomes;
* gameplay rules.

This is an explicit architectural boundary. Difficulty metadata is descriptive data, not part of Basil's scoring or competition logic.

### Why calculate at ingestion time?

Beatmap content is immutable between ingestion operations. If the underlying `.osu` file has not changed, its calculated difficulty metadata has not changed either.

Calculating once and persisting the result therefore avoids repeating an expensive operation for every API request.

The effective model is:

```text
beatmap content
      │
      ▼
ingestion
      │
      ├── parse
      ├── calculate metadata
      └── persist
             │
             ▼
       cheap read requests
```

A client requesting a beatmap must never cause a full difficulty calculation.

## Derived media

Some beatmap assets are generated from the source files rather than stored permanently in every possible form.

For example, clients may request thumbnails at different dimensions or audio previews of different lengths.

Basil generates these representations on demand and caches the resulting data.

This avoids storing every possible derivative for every beatmapset while retaining fast response times after an asset has been requested.

The important distinction is:

```text
Original beatmap files
        │
        ├── authoritative
        │
        └── remain on disk

Derived assets
        │
        ├── generated on demand
        └── cached
```

Derived media must therefore be treated as disposable cache data, not as another source of truth.

## Related code

* [`Basil.Infrastructure/Beatmaps/BeatmapIngestionService.cs`](../../src/Basil.Infrastructure/Beatmaps/BeatmapIngestionService.cs): shared local ingestion pipeline
* [`Basil.Infrastructure/Beatmaps/BeatmapWatcherService.cs`](../../src/Basil.Infrastructure/Beatmaps/BeatmapWatcherService.cs): filesystem change detection
* [`Basil.Infrastructure/Performance/PpyOsuCalculator.cs`](../../src/Basil.Infrastructure/Performance/PpyOsuCalculator.cs): osu! ruleset difficulty calculations
* [`Basil.Web/Routing/Bancho/BeatmapAssetRoutes.cs`](../../src/Basil.Web/Routing/Bancho/BeatmapAssetRoutes.cs): beatmap asset and preview routes

## See also

* [`database.md`](database.md): the `Beatmapsets`/`Beatmaps` schema synchronized by local ingestion
* [`response-envelope.md`](response-envelope.md): how beatmap data is exposed through API responses
* [`configuration.md`](../for-technicians/configuration.md): `Basil:Mirror` configuration and other server settings
