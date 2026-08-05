# Beatmap ingestion

## Overview

Basil never talks to osu!'s servers or a beatmap mirror. Every beatmapset lives on disk next to the executable, and the database mirrors that folder rather than the other way around.

## Why local, offline storage?

A tournament server needs to work on a LAN with no internet access at all: venue Wi-Fi isn't something to depend on mid-tournament. Keeping beatmaps as local files an organizer drops into a folder (or uploads through the admin API) means the server never has a hard dependency on anything outside itself for the maps it serves.

## Contract

- A `.osz` dropped into the `Mapsets/` folder is picked up automatically, extracted into its own subfolder, and reflected into the database within a couple of seconds. No restart or manual step needed.
- A bare `.osu` file at the root is not ingested. A single difficulty file has no beatmapset to belong to, so it's ignored rather than guessed at.
- `POST /beatmapsets` on the admin API accepts `.osz` only, for the same reason.
- Star rating, BPM, length, and max combo are computed locally, once per beatmap at ingestion time, and stored. A client never triggers a recalculation just by reading a beatmap.
- Audio previews require `ffmpeg` on the host. If it's missing or fails, the preview endpoints (`b.<domain>/preview/{id}.mp3` and `api.<domain>`'s equivalent) return `503 Service Unavailable` and log the failure. Nothing else on the server depends on `ffmpeg`, so this never brings anything else down.

## Concepts

- The folder is the source of truth. Every beatmapset gets its own folder (`"{id} Artist - Title"`) holding the original `.osz` contents: audio, images, video, every `.osu`/`.osb`. The database's `Beatmapsets`/`Beatmaps` rows describe what's in that folder; they don't exist independently of it.
- Ingestion runs two ways: a background watcher reacting to filesystem changes in near real time, and a full reconciliation pass on every startup that catches anything that changed while the server was offline.
- No pp, ever. Star rating and difficulty numbers are computed for display only, by referencing osu!'s own ruleset calculation libraries directly. Nothing about scoring, leaderboards, or a match's win condition depends on them. This is a deliberate scope decision, not a missing feature.

## Lifecycle

```
.osz dropped into Mapsets/
        |
BeatmapWatcherService notices the change (~2s)
        |
extract into "{id} Artist - Title/"
        |
compute star rating / BPM / length / object counts
   (via the osu!lazer ruleset libraries, once, per beatmap)
        |
Beatmapsets / Beatmaps rows created or updated
        |
available immediately through GET /beatmapsets and search
```

The same pipeline runs for an admin-uploaded `.osz` (`POST /beatmapsets`) and for a full reconciliation pass at startup: both call into the same ingestion service rather than duplicating the extract-and-analyze logic.

## Design

**Why compute difficulty at ingestion instead of on every request?** A beatmap's content doesn't change unless it's re-ingested, so the number doesn't either. Computing it once and storing it avoids re-running the same calculation on every list/detail request for no benefit.

**Why serve thumbnails and audio previews resized on the fly instead of storing pre-made copies?** Storing every size a client might ever want would multiply disk usage for images that are only actually requested for a fraction of beatmapsets. Resizing (or trimming, for audio previews) on first request and caching the result gets the same steady-state performance without paying the storage cost up front for maps nobody looks at.

## Related code

- `Basil.Infrastructure/Beatmaps/BeatmapIngestionService.cs`
- `Basil.Infrastructure/Beatmaps/BeatmapWatcherService.cs`
- `Basil.Infrastructure/Performance/PpyOsuCalculator.cs`
- `Basil.Web/Routing/Bancho/BeatmapAssetRoutes.cs`

## See also

- [`database.md`](database.md): the `Beatmapsets`/`Beatmaps` tables this pipeline keeps in sync
- [`response-envelope.md`](response-envelope.md): how a beatmap turns into an API response
