# Assets host (`assets.<domain>`)

## Why

`assets.<domain>` centralizes image serving in one middleware. Every image is a stored file served as it is, with a
fixed content type, conditional-request support and a cache lifetime. The query string is ignored: a client cannot ask
for another size or format, so a request never costs more than reading a file, and the only extra work on the disk is
the fixed set of beatmapset cover sizes, each made once.

The host also owns the main-menu banner feature: `GET /menu-content.json`, the JSON manifest that the osu! stable client
fetches during startup. This endpoint naturally belongs with the menu assets it describes.

Write operations remain on `api.<domain>` and require a token with the matching privilege. `assets.<domain>` is read-only. When an asset has
moved to `assets.<domain>`, a corresponding `GET` on `api.<domain>` redirects to the new location.

## What

`assets.<domain>` serves:

* `GET /menu-content.json` — the main-menu banner manifest. Its path and field names are fixed by the protocol,
  including the client's PascalCase `IsCurrent` field. See `MenuContentMiddleware`. A banner whose stored image is
  missing is left out of the manifest; the HTTP API and the console still list it, so it can be deleted.

* `GET /menu/banners/{fileName}` — menu banners.

* `GET /menu/seasonals/{fileName}` — seasonal menu images.

* `GET /menu/icon` — the menu icon.

  Menu images are served as stored; a query string changes nothing.

* `GET /menu/seasonals` — lists seasonal files by bare filename. `osu.<domain>/web/osu-getseasonal.php` uses this
  listing to construct its URLs. The list is the content of the seasonals folder, so an image placed there is served
  at once.

A fresh install has one banner (`default-banner@2x.png`, always current, opening the project's GitHub page) and one
seasonal background (`github-banner.png`). Both files ship in the program's `Data` folder, where stored files always live, and the database starts
with the banner's record.

* `GET /beatmapsets/{beatmapsetId}/background` — beatmapset background.

* `GET /beatmapsets/{beatmapId}/background` — beatmap background.

* `GET /beatmapsets/{beatmapsetId}/covers/{variant}.jpg` — fixed-size cover variants: `cover`, `card`, `list`, and
  `slimcover`.

* `GET /beatmapsets/{beatmapsetId}/{audio,video,download,storyboard,audiopreview,...}` — non-image beatmapset files.

  These files are served directly with `Results.File` and `enableRangeProcessing: true`, allowing HTTP Range requests
  and `206 Partial Content` responses for large downloads.

`b.<domain>` and `a.<domain>` serve beatmap thumbnails and avatars the same way. Their URLs remain unchanged because
the real osu! stable client hardcodes these hosts and paths as part of the osu! (stable) protocol.

Every served image carries `Content-Type` (detected from the first bytes of the stored file, never from its name),
`ETag` and `Last-Modified` (a conditional request answers `304`), `X-Content-Type-Options: nosniff` and
`Cache-Control: public,max-age=604800` (7 days; avatars on `a.<domain>` use `public,max-age=300`, since users replace
them). A stored file that is not a PNG, JPEG, GIF, WebP or BMP picture is a `404`.

### Not covered

Basil intentionally does not implement the asset families provided by `assets.ppy.sh` for user-profile covers, team
flags/headers, profile badges, contests, or artist/media assets. Basil does not have the corresponding public profile,
team, contest, or artist systems that would require them.

This follows the project's scope rules documented in [`working-scopes.md`](working-scopes.md).

## How

### Serving

`UseRestImages()` adds `ImageAdmissionMiddleware` (the `Asset` admission class), the preview and menu-manifest
middlewares and `ImageServingMiddleware`. They run as normal middleware before the host-based endpoint routing.

The same path may exist on several hosts (`/beatmapsets/{beatmapsetId}/background` is a real asset route on
`assets.<domain>` and a redirect on `api.<domain>`), so `ImageServingMiddleware` checks the host label before the path:

| Host        | Path                                                            | Serves                                       |
|-------------|-----------------------------------------------------------------|----------------------------------------------|
| `assets.`   | `/menu/banners/{file}`, `/menu/seasonals/{file}`, `/menu/icon`  | menu images                                  |
| `assets.`   | `/beatmapsets/{id}/covers/{cover,card,list,slimcover}.jpg`      | beatmapset cover variants                    |
| `b.`        | `/thumb/{setId}.jpg`, `/thumb/{setId}l.jpg`                     | beatmapset thumbnails                        |
| `a.`        | `/{userId}`                                                     | the user's avatar, or the default avatar     |
| `osu.`      | `/ss/{name}`                                                    | uploaded screenshots                         |

The middleware only routes: it sends a message (`OpenContentImage`, `OpenUserAvatar`, `OpenBeatmapsetCover`) and
writes the `ImageFile` that comes back. It returns no result when the image cannot be served (a private beatmapset, a
missing file, an icon that points to an external URL); the request then falls through to the minimal-API route of the
same host, which keeps the private-resource checks. The routes are in `Basil.Host.Rest/Routes/` (`MenuRoutes`,
`BeatmapRoutes`, `UserRoutes`) and `Basil.Host.Stable/Web/OsuWebRoutes.cs`.

The default avatar is an embedded resource, read once.

### Beatmapset cover variants

The osu! client requests some images using bare paths. The sizes are fixed, and each is cropped to fill:

| Variant                 | Size       |
|-------------------------|------------|
| `cover`                 | 900 x 250  |
| `card`                  | 400 x 140  |
| `list`                  | 150 x 150  |
| `slimcover`             | 1920 x 360 |
| thumbnail (`/thumb/{id}.jpg`)  | 80 x 60    |
| large thumbnail (`/thumb/{id}l.jpg`) | 160 x 120 |

The sizes are best-effort approximations; no verified osu! web dimensions were available.

The Beatmaps module makes a variant from the set's background on its first request, as a JPEG in the `covers` folder
of the set's own folder. One request makes it while the others wait for it; the file is written under a temporary name
and moved into place. Only PNG and JPEG backgrounds up to 8192 pixels per side are decoded (backgrounds come from
uploaded archives); any other background has no cover (`404`). The `covers` folder goes away with the set's folder
(delete, reimport) and is cleared when the set's files are cleared or a background is saved; a variant older than the
background it was made from is made again.

The osu! client never sends a size: there is no command for it, so there is no cost a client can multiply.

### Asset references

The server refers to an asset by a `ContentReference` (Domain), never by a bare string or URI. A reference is one of:

* the path of a file the server stores, relative to the data directory (`Menu/Banners/summer.png`), so it stays
  valid when the installation moves to another machine;
* the absolute `http`/`https` address of an asset hosted elsewhere, kept exactly as given; Basil never copies it.

The value is canonical: `\` becomes `/`, empty and `.` segments are dropped, and a rooted path, a `..` segment, a
drive or scheme (`file:`, `javascript:`), or a control character is rejected. A banner's image and the menu icon are
stored in the database as the reference's text.

### Storages and serving

Each kind of stored asset has a storage contract (`IMenuBannerStorage`, `IMenuSeasonalsStorage`, `IMenuIconStorage`,
`IScreenshotStorage`) with list, save, rename where it applies, and delete; each save returns the reference of the
stored file. `OpenAsync` serves the server's own reads only. The storages keep no file content in memory and are not
resident: the directory is the index and is read as it is when asked.

Clients never read through the storages. The hosts serve stored files under a fixed public path per asset directory
(for example `menu/banners/{name}` for `Data/Menu/Banners/{name}`), with a static file provider rooted at that asset
directory and never at the whole `Data/` directory, which also holds replays, FAQs and the cache. A reference to an
external address is handed to the client as it is.

Screenshots are stored under a new UUIDv7 name with the upload's extension.

An uploaded avatar is accepted only when its content is a PNG or JPEG picture of at most `Uploads.AvatarMaxSide` pixels
per side and at most `Uploads.AvatarMaxSize` bytes; the file name is not consulted. A refused upload leaves the user's
previous avatar in place.

### Data layout

Menu assets use the following layout:

* `Data/Menu/Banners/` — menu banners
* `Data/Menu/Seasonals/` — seasonal images
* `Data/Menu/Icon/` — the current menu icon, the only file there
* `Data/Screenshots/` — uploaded screenshots

`MenuBanners` stores banner metadata in the database; see [`database.md`](database.md).

Seasonal images remain a plain file listing rather than a database-backed collection, preserving the existing design.

See [`configuration.md`](../for-technicians/configuration.md) for the complete `Data/` directory layout.

## Related code

* `Basil.Host.Rest/Images/` — `ImageHosts` (admission) and `ImageServingMiddleware` (the image hosts).
* `Basil.Infrastructure.Beatmaps/Storage/BeatmapsetCovers.cs` — the cover variants.
* `Basil.Host.Rest/Routes/MenuRoutes.cs` — menu banner, icon and seasonal routes.
* `Basil.Application.Content/Storage/` — the storage ports for avatars, menu assets, screenshots and the FAQ.
* `Basil.Infrastructure.Content/Storage/` — their file implementations.
* `Basil.Domain/Content/MenuBanner.cs` — banner metadata model.
