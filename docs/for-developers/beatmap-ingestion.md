# Beatmap ingestion

## Overview

Basil serves beatmaps only from the local `Data/Beatmapsets/` directory. There is no mirror, no osu! API source and no
redirect to ppy: what the server does not hold answers 404 (media, `.osu` files, archives).

A beatmapset placed in `Beatmapsets/` is ingested, analyzed and indexed, and what is indexed is what osu!direct search,
downloads and the beatmap routes serve.

## Ingestion contract

The ingestion pipeline accepts **beatmapsets**, not individual difficulty files.

* A `.osz` placed in `Beatmapsets/` is automatically detected, moved into a folder of its own as the beatmapset's permanent storage, analyzed, and synchronized with the database. No restart or manual action is required.
* A standalone `.osu` file placed directly in `Beatmapsets/` is ignored. Without the containing beatmapset, Basil cannot reliably determine its ownership and does not attempt to infer one.
* `POST /v1/beatmapsets` accepts `.osz` files only and uses the same ingestion pipeline as filesystem ingestion.
* Star rating, BPM, length, and object counts are calculated once during ingestion and persisted with the beatmap metadata. Reading the beatmap files needs the osu! calculation stack in `Data/Rulesets/`; without it an import is refused (`rulesetsMissing`) and a waiting `.osz` stays where it is (see below).
* Reading a beatmap never triggers difficulty recalculation.

### Audio previews are optional

Audio previews are built with [libsndfile](https://libsndfile.github.io/libsndfile/) through NAudio. The system
`libsndfile` library must be present; the container image includes it.

If preview generation fails:

* the preview does not exist, so its endpoint returns `404`;
* the failure is logged;
* the rest of beatmap ingestion and serving continues normally.

## Source of truth

Each locally ingested beatmapset has one folder of its own, holding its archive exactly as uploaded and the files
made from it:

```text
Beatmapsets/
└── {id} Artist - Title/
    ├── archive.osz            the archive as it was given
    ├── archive-novideo.osz    the same archive without its video entries (the `n` download)
    ├── storyboard.osb         the set's storyboard
    ├── versions/{md5}.osu     one file per difficulty
    ├── backgrounds/           files of the archive, under their sanitized names
    ├── audio/                 likewise, plus generated previews
    ├── video/                 likewise
```

Files under `backgrounds/`, `audio/` and `video/` keep the name they have in the archive, made safe for the file
system; nested files with the same name get a numeric suffix (`-2`, `-3`, ...). Local ids start at 1 000 000 000.

A loose `.osz` placed at the top of `Beatmapsets/` is moved into such a folder when it is taken in. A folder that
holds neither an archive nor files made from one is not a beatmapset and is ignored.

The database mirrors this filesystem state:

```text
Beatmapsets
    │
    └── Beatmaps
```

The `Beatmapsets` and `Beatmaps` rows describe the locally ingested beatmap data. They do not replace the source files on disk.

This distinction is important when modifying the ingestion pipeline:

> **For locally ingested beatmaps, the filesystem is authoritative and the database is its indexed representation.**

## Ingestion triggers

Basil has two mechanisms (the upload route is the third entry into the same pipeline) for keeping the local beatmap database synchronized with `Beatmapsets/`.

### Filesystem watcher

[`BeatmapsetStorageWatcher`](../../Basil.Infrastructure.Beatmaps/Workers/BeatmapsetStorageWatcher.cs) monitors the directory for a `.osz` archive appearing, changing, or disappearing.

When a new `.osz` appears, the watcher schedules it for ingestion. Under normal conditions, the change becomes visible within a few seconds.


### Startup reconciliation

The watcher cannot account for changes that happen while Basil is offline.

Therefore, Basil performs a full scan during startup, which finishes before the server serves clients. This discovers beatmapsets that were added, removed, or otherwise changed while the server was not running.

Every mechanism ends in the same importer and scanner of `Basil.Application.Beatmaps`. There is no separate implementation for startup discovery, filesystem changes, or uploads.

## Ingestion lifecycle

```text
.osz
 │
 ├── Beatmapsets/ filesystem
 │       │
 │       └── BeatmapsetStorageWatcher
 │
 └── POST /beatmapsets
         │
         ▼
BeatmapsetImporter / BeatmapsetScanner
         │
         ├── keep archive in place as canonical storage
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
BeatmapsetScanner
```

## Difficulty calculation

Basil calculates beatmap difficulty with osu!'s ruleset calculation libraries, in the Rulesets module. A beatmap is
stored first, with no difficulty (`null`, "not calculated yet"); the import then asks the rulesets for every beatmap
that has none and waits, for one deadline of `beatmaps.beatmapWait` for the whole set, for the answers. What arrived is
stored, and the rest stays `null` until its late answer is stored. The osu! client gets zeros for a difficulty that is
`null`. At start-up, and when the calculation stack is installed or the `reload` command runs, the beatmaps without
a difficulty (or all of them) are asked for again. Without the stack (see [architecture.md](architecture.md)) the answer
is nothing and the difficulty stays `null`; an import is refused with `rulesetsMissing`, and the storage scans leave
everything as it is, so a waiting `.osz` is taken in by the scan that follows the installation.

The difficulty attributes of a beatmap file under a mode and a set of difficulty-affecting mods are kept as files in
`Cache/Attributes`, one per combination: the first request for a combination calculates and writes it, later
requests read it. A file written by another ruleset version or damaged is calculated again; the files are a cache and
outlive the set. Concurrent requests for the same combination share one calculation, and calculations of different
combinations are limited to the number of processor cores.

A file is `Attributes/{md5}/{mode}/{mask}.json`. `mode` is the mode played, and `mask` is the play's mods reduced to the
ones that change the difficulty (`GameModsGroups.Difficulty`, `ReduceToDifficulty`): Nightcore counts as DoubleTime, and
HD, NF, SD and the like share the NoMod file where they do not change the difficulty. The content is the version of the
ruleset package and calculator, the `(id, value)` pairs the calculator gives for storage and the beatmap values the
calculation needs on reading. `IBeatmapCalculator` is the port to this: it answers stars and pp as plain numbers.

The server also calculates pp for scores from these attributes; see the pp rule in `AGENTS.md`. Star rating and pp are display-only: they have no effect on

* score validation;
* leaderboards and rankings;
* multiplayer results and match outcomes;
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

* [`BeatmapsetImporter.cs`](../../Basil.Application.Beatmaps/Logic/BeatmapsetImporter.cs): imports one uploaded `.osz`
* [`BeatmapsetScanner.cs`](../../Basil.Application.Beatmaps/Logic/BeatmapsetScanner.cs): scans the storage and reconciles it with the database
* [`BeatmapsetPlanner.cs`](../../Basil.Application.Beatmaps/Logic/BeatmapsetPlanner.cs): decides what a changed archive means for the stored records
* [`BeatmapsetStorageWatcher.cs`](../../Basil.Infrastructure.Beatmaps/Workers/BeatmapsetStorageWatcher.cs): startup scan and filesystem change detection
* [`FileBeatmapsetStorage.cs`](../../Basil.Infrastructure.Beatmaps/Storage/FileBeatmapsetStorage.cs): the `Beatmapsets/` directory as storage
* [`OsuBeatmapAnalyser.cs`](../../Basil.Infrastructure.Rulesets/Adapters/OsuBeatmapAnalyser.cs): osu! ruleset difficulty calculations
* [`BeatmapPreviewMaker.cs`](../../Basil.Infrastructure.Beatmaps/Adapters/BeatmapPreviewMaker.cs): audio previews with NAudio and libsndfile
* [`BeatmapRoutes.cs`](../../Basil.Host.Rest/Routes/BeatmapRoutes.cs): beatmapset routes of the API host

## See also

* [`database.md`](database.md): the `Beatmapsets`/`Beatmaps` schema synchronized by local ingestion
* [`response-envelope.md`](response-envelope.md): how beatmap data is exposed through API responses
* [`configuration.md`](../for-technicians/configuration.md): server settings
