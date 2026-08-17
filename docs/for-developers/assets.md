# Assets host (`assets.<domain>`)

## Why

Before this host existed, every image Basil served (seasonal backgrounds, the menu icon, beatmapset
covers) was a hand-written `Results.File` call on `api.<domain>`, with a separate hand-rolled resizer
and cache for the two beatmap thumbnail sizes on `b.<domain>`. Adding a new image size meant writing a
new resize-and-cache code path by hand.

`assets.<domain>` centralizes image serving behind [ImageSharp.Web](https://docs.sixlabors.com/articles/imagesharp.web/gettingstarted.html):
one middleware pipeline, one on-disk cache, and resize/crop support driven by request commands instead
of bespoke code per image family. It is also where the main-menu banner feature lives —
`GET /menu-content.json`, the JSON manifest osu! stable's client fetches on startup — since that
feature has no other natural home.

`api.<domain>` keeps admin-key-gated write routes (create/replace/delete) for everything under this
host; `assets.<domain>` only ever serves reads. A `GET` on `api.<domain>` for something that now lives
on `assets.<domain>` redirects there.

## What

`assets.<domain>` serves:

* `GET /menu-content.json` — the main-menu banner manifest (protocol-fixed path and field names,
  including the client's PascalCase `IsCurrent`; see `MenuContentRoutes`).
* `GET /menu/banners/{fileName}`, `GET /menu/seasonals/{fileName}`, `GET /menu/icon` — menu images,
  resizable via ImageSharp.Web's usual query-string commands (`?width=`, `?height=`, `?rmode=`, ...).
* `GET /menu/seasonals` — the bare-filename listing `osu.<domain>/web/osu-getseasonal.php` builds its
  URLs from.
* `GET /beatmapsets/{mapsetId}/background`, `.../{beatmapId}/background`,
  `.../covers/{variant}.jpg` (`cover`/`card`/`list`/`slimcover`) — beatmapset/beatmap backgrounds and
  fixed-size cover crops.
* `GET /beatmapsets/{mapsetId}/{audio,video,download,storyboard,audiopreview,...}` — the non-image
  beatmapset files. These never go through ImageSharp.Web; they're plain `Results.File` calls with
  `enableRangeProcessing: true` for HTTP Range/206 support on large downloads.

`b.<domain>` (beatmap thumbnails) and `a.<domain>` (avatars) also serve through ImageSharp.Web now, but
their URLs did **not** move to `assets.<domain>` — the real osu! stable client hardcodes those hosts
and exact paths as part of the bancho protocol, so they stay put. Only the serving *mechanism*
changed.

### Not covered

Basil intentionally does not replicate `assets.ppy.sh`'s user-profile-cover, team flag/header,
profile-badge, contest, or artist/media asset families — Basil has no public profile pages, teams, or
contest system for them to back. See
[`working-scopes.md`](working-scopes.md) for the scope rule this follows.

## How

### Host-gating providers

`UseImageSharp()` runs as ordinary middleware, ahead of `RequireHost`-based endpoint routing. That
means an `IImageProvider` that only checks the request *path* can match a request meant for a
different host — several paths exist both as a redirect on `api.` and as a real file on `assets.`
(`/beatmapsets/{mapsetId}/background` is one). Every provider therefore checks the host first, via
`AssetsHost.Matches` (`Basil.Infrastructure/Media/Assets/AssetsHost.cs`).

### Providers

| Provider | Host | Serves |
|---|---|---|
| `MenuAssetImageProvider` | `assets.` | `/menu/banners/{file}`, `/menu/seasonals/{file}` |
| `MenuIconImageProvider` | `assets.` | `/menu/icon`, only when it's an uploaded file |
| `BeatmapThumbnailImageProvider` | `b.` | `/thumb/{setId}.jpg`, `/thumb/{setId}l.jpg` |
| `AvatarImageProvider` | `a.` | `/{userId}`, only when the user has uploaded an avatar |
| `BeatmapsetBackgroundImageProvider` | `assets.` | beatmap/beatmapset backgrounds, `covers/{variant}.jpg` |

A provider that doesn't match (private beatmapset, no local file, external-URL icon, no uploaded
avatar) returns no result, and the request falls through to a plain minimal-API route registered on
the same host, which reproduces the same private-check/mirror-fallback behavior the old hand-written
handlers had. `AvatarRoutes` and `BeatmapAssetRoutes`/`BeatmapsetAssetRoutes` document this per-route.

### Fixed-size crops without query strings

`b.<domain>/thumb/{id}.jpg` and `assets.<domain>/beatmapsets/{id}/covers/{variant}.jpg` carry no query
string — the real client (or Basil's own redirects) hits the bare path. `ConfigureImageSharp` in
`Program.cs` injects the fixed `width`/`height`/`rmode=crop` commands via
`ImageSharpMiddlewareOptions.OnParseCommandsAsync`, based on the request path
(`BeatmapThumbnailImageProvider.TryGetSize` / `BeatmapsetBackgroundImageProvider.TryGetCoverSize`).
This works because `OnParseCommandsAsync` runs *before* the middleware's "no commands → pass through"
check, so injecting commands there still triggers processing.

Cover crop sizes are a best-effort approximation — no verified primary-source dimensions from osu!
web were available when this was implemented. Adjust `TryGetCoverSize` if real values are confirmed
later.

### Cache

ImageSharp.Web's own cache lives under `Data/Cache/imagesharp/`, configured via
`PhysicalFileSystemCacheOptions` alongside the existing `Data/Cache/` folder (still used for
transcoded audio previews, which ImageSharp.Web doesn't touch). `BrowserMaxAge`/`CacheMaxAge` are set
long, since ImageSharp.Web invalidates its cache by the source file's last-write-time. Avatars get a
shorter browser `Cache-Control` via `OnPrepareResponseAsync`, since they change with user behavior
(re-uploads) rather than staying static like menu/beatmap images.

### Data layout

Menu images live under `Data/Menu/{Banners,Seasonals}/` (plural folders) and `Data/Menu/Icon{ext}` (a
single file — there's only ever one icon). `MenuBanners` is a database table (see
[`database.md`](database.md)); `Data/Menu/Seasonals/` stays plain file listing, matching its
pre-existing design.

Basil moves `Data/Seasonals/` → `Data/Menu/Seasonals/` and `Data/MenuIcon.{ext}` →
`Data/Menu/Icon{ext}` automatically on startup, once, if it finds the old layout. See
[`configuration.md`](../for-technicians/configuration.md) for the full `Data/` directory reference.

## Related code

* `src/Basil.Infrastructure/Media/Assets/` — `AssetsHost`, every `IImageProvider`, and
  `PhysicalFileImageResolver`.
* `src/Basil.Web/Routing/Assets/` — `AssetsHostRoutes`, `MenuAssetRoutes`, `MenuContentRoutes`,
  `BeatmapsetAssetRoutes`.
* `src/Basil.Web/Program.cs` — `ConfigureImageSharp`.
* `src/Basil.Domain/Content/MenuBanner.cs`, `src/Basil.Application/Services/Content/MenuBannerService.cs`
  — the banner metadata model.
