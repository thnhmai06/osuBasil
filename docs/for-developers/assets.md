# Assets host (`assets.<domain>`)

## Why

Previously, Basil served every image directly from `api.<domain>` using hand-written `Results.File` handlers. Beatmap
thumbnails on `b.<domain>` had a separate, custom resize-and-cache implementation, so adding another image size required
another bespoke code path.

`assets.<domain>` centralizes image serving
around [ImageSharp.Web](https://docs.sixlabors.com/articles/imagesharp.web/gettingstarted.html). Image resizing,
cropping, and caching now share a single middleware pipeline and on-disk cache, with transformations controlled by
request commands instead of custom logic for each image type.

The host also owns the main-menu banner feature: `GET /menu-content.json`, the JSON manifest that the osu! stable client
fetches during startup. This endpoint naturally belongs with the menu assets it describes.

Write operations remain on `api.<domain>` and require an admin key. `assets.<domain>` is read-only. When an asset has
moved to `assets.<domain>`, a corresponding `GET` on `api.<domain>` redirects to the new location.

## What

`assets.<domain>` serves:

* `GET /menu-content.json` — the main-menu banner manifest. Its path and field names are fixed by the protocol,
  including the client's PascalCase `IsCurrent` field. See `MenuContentRoutes`.

* `GET /menu/banners/{fileName}` — menu banners.

* `GET /menu/seasonals/{fileName}` — seasonal menu images.

* `GET /menu/icon` — the menu icon.

  Menu images are processed by ImageSharp.Web and support its usual query-string commands such as `?width=`, `?height=`,
  and `?rmode=`.

* `GET /menu/seasonals` — lists seasonal files by bare filename. `osu.<domain>/web/osu-getseasonal.php` uses this
  listing to construct its URLs.

* `GET /beatmapsets/{mapsetId}/background` — beatmapset background.

* `GET /beatmapsets/{beatmapId}/background` — beatmap background.

* `GET /beatmapsets/{mapsetId}/covers/{variant}.jpg` — fixed-size cover variants: `cover`, `card`, `list`, and
  `slimcover`.

* `GET /beatmapsets/{mapsetId}/{audio,video,download,storyboard,audiopreview,...}` — non-image beatmapset files.

  These files bypass ImageSharp.Web and are served directly with `Results.File` and `enableRangeProcessing: true`,
  allowing HTTP Range requests and `206 Partial Content` responses for large downloads.

`b.<domain>` and `a.<domain>` also use ImageSharp.Web for beatmap thumbnails and avatars respectively. Their URLs remain
unchanged because the real osu! stable client hardcodes these hosts and paths as part of the Bancho protocol. Only the
serving mechanism changed.

### Not covered

Basil intentionally does not implement the asset families provided by `assets.ppy.sh` for user-profile covers, team
flags/headers, profile badges, contests, or artist/media assets. Basil does not have the corresponding public profile,
team, contest, or artist systems that would require them.

This follows the project's scope rules documented in [`working-scopes.md`](working-scopes.md).

## How

### Host gating

`UseImageSharp()` runs as normal middleware before the `RequireHost`-based endpoint routing.

This means an `IImageProvider` cannot rely on the request path alone. The same path may exist on multiple hosts—for
example, `/beatmapsets/{mapsetId}/background` exists both as a real asset route on `assets.<domain>` and as a redirect
on `api.<domain>`.

Every provider therefore checks the host before matching the path, using `AssetsHost.Matches` from
`Basil.Infrastructure/Media/Assets/AssetsHost.cs`.

### Providers

| Provider                       | Host      | Serves                                                    |
|--------------------------------|-----------|-----------------------------------------------------------|
| `MenuBannersProvider`          | `assets.` | `/menu/banners/{file}`                                    |
| `MenuSeasonalsProvider`        | `assets.` | `/menu/seasonals/{file}`                                  |
| `MenuIconProvider`             | `assets.` | `/menu/icon`, when an uploaded file exists                |
| `BeatmapThumbnailProvider`     | `b.`      | `/thumb/{setId}.jpg`, `/thumb/{setId}l.jpg`               |
| `AvatarProvider`               | `a.`      | `/{userId}`, when the user has uploaded an avatar         |
| `BeatmapsetBackgroundProvider` | `assets.` | Beatmap/beatmapset backgrounds and `covers/{variant}.jpg` |

Providers return no result when they cannot serve a request—for example, when a beatmapset is private, a local file does
not exist, an icon points to an external URL, or a user has no uploaded avatar.

The request then falls through to the corresponding minimal-API route on the same host. These routes preserve the
behavior of the previous hand-written handlers, including private-resource checks and mirror fallbacks.

The relevant behavior is documented in `AvatarRoutes`, `BeatmapAssetRoutes`, and `BeatmapsetAssetRoutes`.

### Fixed-size crops without query strings

The real osu! client requests some images using bare paths with no query string:

* `b.<domain>/thumb/{id}.jpg`
* `assets.<domain>/beatmapsets/{id}/covers/{variant}.jpg`

`ConfigureImageSharp` in `Program.cs` injects the required `width`, `height`, and `rmode=crop` commands through
`ImageSharpMiddlewareOptions.OnParseCommandsAsync`.

The dimensions are selected from the request path using:

* `BeatmapThumbnailProvider.TryGetSize`
* `BeatmapsetBackgroundProvider.TryGetCoverSize`

This works because `OnParseCommandsAsync` executes before ImageSharp.Web checks whether any commands were supplied.
Injecting commands there therefore causes ImageSharp.Web to process the request even though the original URL contains no
query string.

Cover dimensions are currently best-effort approximations because no verified primary-source dimensions from osu! web
were available when this was implemented. `TryGetCoverSize` should be updated if the actual dimensions are confirmed
later.

### Cache

ImageSharp.Web stores its cache under:

`Data/Cache/imagesharp/`

It uses `PhysicalFileSystemCacheOptions` alongside Basil's existing `Data/Cache/` directory. The rest of `Data/Cache/`
is still used for transcoded audio previews, which are unrelated to ImageSharp.Web.

`BrowserMaxAge` and `CacheMaxAge` are configured with long lifetimes because ImageSharp.Web invalidates cached images
when the source file's last-write time changes.

Avatars use a shorter browser `Cache-Control` lifetime through `OnPrepareResponseAsync`, since users may replace their
avatars. Menu and beatmap images are comparatively static.

### Data layout

Menu assets use the following layout:

* `Data/Menu/Banners/` — menu banners
* `Data/Menu/Seasonals/` — seasonal images
* `Data/Menu/Icon{ext}` — the current menu icon

`MenuBanners` stores banner metadata in the database; see [`database.md`](database.md).

Seasonal images remain a plain file listing rather than a database-backed collection, preserving the existing design.

Basil automatically migrates the old layout to the new one once at startup when the old paths are detected:

* `Data/Seasonals/` → `Data/Menu/Seasonals/`
* `Data/MenuIcon.{ext}` → `Data/Menu/Icon{ext}`

See [`configuration.md`](../for-technicians/configuration.md) for the complete `Data/` directory layout.

## Related code

* `src/Basil.Infrastructure/Media/Assets/` — `AssetsHost`, all `IImageProvider` implementations, and
  `PhysicalImageResolver`.
* `src/Basil.Web/Routing/Assets/` — `AssetsHostRoutes`, `MenuAssetRoutes`, `MenuContentRoutes`, and
  `BeatmapsetAssetRoutes`.
* `src/Basil.Web/Program.cs` — `ConfigureImageSharp`.
* `src/Basil.Domain/Content/MenuBanner.cs` — banner metadata model.
* `src/Basil.Application/Services/Content/MenuBannerService.cs` — banner metadata service.
