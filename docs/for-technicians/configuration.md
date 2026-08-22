# Configuration

This page is the authoritative reference for configuring and maintaining a Basil server. It covers application settings,
the admin key, and persistent server data.

If this document conflicts with another document, **this document takes precedence**.

## Configuration

### Configuration file

Basil's configuration is stored in [`appsettings.json`](../../src/Basil.Web/appsettings.json), under the `Basil` section.

The only setting outside `Basil` is `AllowedHosts`, which is an ASP.NET Core framework setting.

### Configuration precedence

Basil loads configuration from multiple sources. Later sources override earlier ones:

1. `appsettings.json`
2. `appsettings.{Environment}.json`, if present

This is commonly used in container deployments to override values from a mounted `appsettings.json`.

### Available settings

All settings below are under the `Basil` section.

| Setting                   | Description                                                                                                                                                                                        |
|---------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Server:Domain`           | Public hostname of the Basil server. All Basil subdomains are derived from this domain.                                                                                                            |
| `Server:Port`             | HTTPS port used by Basil. Defaults to `443`. **This is the port that osu! client will connect to, so don't change it unless you are sure you know what you are doing.**                                |
| `Server:CertPath`         | Path to the HTTPS PFX certificate. Leave unset when TLS is handled by the ASP.NET Core development certificate or a reverse proxy. See [`https.md`](https.md).                                     |
| `Server:CertPassword`     | Password for the PFX certificate specified by `Server:CertPath`.                                                                                                                                   |
| `Bot:Name`                | Display name of BasilBot. Changing it after the first startup also renames the seeded `id=0` user.                                                                                                 |
| `Bot:CommandPrefix`       | Prefix required for BasilBot commands, such as `!help`, `!roll`, and `!mp`.                                                                                                                        |
| `Bot:Country`             | BasilBot's country code. Defaults to `vn`.                                                                                                                                                         |
| `Irc:Name`                | IRC server name.                                                                                                                                                                                   |
| `Irc:Port`                | TCP port used by Basil's embedded IRC gateway. Defaults to `6667`.                                                                                                                                 |
| `Mirror:DownloadEndpoint` | Optional external `.osz` download mirror. Basil always checks local mapsets first. If a mapset with a valid ppy ID is not available locally, Basil can redirect the download to this mirror.       |
| `Mirror:SearchEndpoint`   | Optional osu!direct search mirror. Without this setting, searches use local mapsets only. When configured, Basil searches the mirror and falls back to local storage if the mirror is unreachable. |
| `Logging:MinimumLevel`    | Minimum log level written to stdout and the full log file. Defaults to `Information`. See [`logging.md`](logging.md).                                                                              |

### Applying configuration changes

For changes made to `appsettings.json`:

1. Stop Basil.
2. Edit `appsettings.json`.
3. Start Basil again.
4. Check the startup log for configuration or certificate errors.

No rebuild is required.

For container deployments, prefer environment variables for deployment-specific values such as the domain, port, or
certificate settings. See [`docker.md`](docker.md).

---

## Admin Key

The admin key protects administrative operations on the `api.<domain>` host and is also used as the secret for in-game
registration through the osu! client's **Email** field.

The key is:

* stored as a bcrypt hash in the database;
* sent to the API as `Authorization: Bearer <key>`;
* managed through the `api.<domain>` `/adminkey` endpoints.

### Set the admin key

A new database has **no admin key** configured.

Before exposing the server to users, set an admin key with:

```text
PUT /adminkey
```

Once a key is set, administrative requests must provide:

```text
Authorization: Bearer <key>
```

Changing the key does **not** require a restart. The new key is effective on the next request.

### No-key / bypass mode

If no admin key is configured, Basil runs in **bypass mode**.

In bypass mode:

* admin-protected API operations do not require authentication;
* in-game registration does not require an admin key;
* Basil logs a warning at startup.

**Do not expose a production server without an admin key.**

Set one with `PUT /adminkey` to leave bypass mode.

---

## Persistent data

Basil creates a `Data/` directory next to the executable on startup if it does not already exist.

The contents of `Data/` are **server state** and must normally be preserved when moving or upgrading a Basil
installation.

| Path                      | Contents                                                                                                                          | Safe to delete?                                            |
|---------------------------|-----------------------------------------------------------------------------------------------------------------------------------|-------------------------------------------------------------|
| `Data/Basil.db`           | Main SQLite database. `-wal` and `-shm` files may exist while Basil is running. Database migrations run automatically at startup. | **No**                                                       |
| `Data/Replays/`           | Submitted score replay files.                                                                                                     | **No**, unless you intentionally want to remove replays.    |
| `Data/Avatars/`           | User avatar files.                                                                                                                | **No**, unless you intentionally want to remove avatars.    |
| `Data/Mapsets/`           | Installed beatmapsets. Each set has its own directory containing the original `.osz` contents.                                    | **No**, unless you intentionally want to remove beatmaps.   |
| `Data/Menu/Seasonals/`    | Seasonal background images used by the client's login screen.                                                                     | Optional                                                     |
| `Data/Menu/Banners/`      | Main-menu banner images uploaded for `MenuBanners` entries with a local image (as opposed to an external URL).                    | Optional                                                     |
| `Data/Menu/Icon{ext}`     | Uploaded custom main-menu icon image, when one is configured as a file upload.                                                    | Usually no                                                   |
| `Data/Faqs/`              | Text files used by `!faq <entry>`.                                                                                                | Optional                                                     |
| `Data/Cache/`             | Generated thumbnails, cover crops, and transcoded audio previews.                                                                 | **Yes**                                                     |

`Data/Seasonals/` and `Data/MenuIcon.{ext}` are the pre-`assets.<domain>` locations of the two rows
above; Basil moves them into place automatically on startup if found (see
[`assets.md`](../for-developers/assets.md)).

### Database

`Data/Basil.db` is the primary server database.

**Do not copy, replace, or delete the database while Basil is running.**

The database may also have:

```text
Data/Basil.db-wal
Data/Basil.db-shm
```

These are SQLite runtime files. Always stop Basil before copying the database and its associated files.

Database migrations are applied automatically when Basil starts.

### Beatmaps

Beatmaps are stored under:

```text
Data/Mapsets/
```

Each mapset is stored as a directory containing the extracted contents of the original `.osz` archive, including audio,
images, video, and `.osu` files.

You can add an `.osz` file directly to the `Data/Mapsets/` directory. Basil automatically extracts it into its mapset
directory.

A standalone `.osu` file is **not** imported because a difficulty file alone does not provide enough information to
identify its beatmapset.

Mapset changes are detected automatically while Basil is running. A background watcher normally processes additions,
changes, and deletions within approximately two seconds. Basil also performs a full reconciliation during startup.

Administrators can upload `.osz` files through:

```text
POST /beatmaps
```

on the `api.<domain>` host. This endpoint requires the admin key.

### Cache

`Data/Cache/` contains generated data such as:

* resized beatmap thumbnails and cover crops (`Data/Cache/imagesharp/`, managed by ImageSharp.Web);
* transcoded audio previews.

Cache files are regenerated automatically when required.

**The cache can be deleted safely while Basil is stopped.** Do not delete other `Data/` directories as a substitute for
clearing cache.

### Menu icon

A custom menu icon may be stored as:

```text
Data/Menu/Icon{ext}
```

This file exists only when the icon was uploaded to Basil.

The icon's enabled/disabled state and click-through URL are stored in the database, not in this file.

Menu icons are managed through the `/menu/icon` and `/menu/icon/image` endpoints on the `api.<domain>` host, and served
publicly from `assets.<domain>/menu/icon`. See [`assets.md`](../for-developers/assets.md).

---

## Moving or upgrading a server

Treat the executable files and `Data/` as separate categories:

* **Executable directory**: application files and default configuration.
* **`Data/`**: persistent server state.
* **`Logs/`**: operational history; normally does not need to be migrated.

### Full migration

To move Basil to another server:

1. Stop Basil on the source server.
2. Copy the entire Basil executable directory to the target.
3. Make sure `appsettings.json` and the complete `Data/` directory are included.
4. Do **not** copy `Logs/` unless you specifically need the old log history.
5. Verify the target's configuration, especially domain, HTTPS certificate, and ports.
6. Start Basil on the target.
7. Check the startup log for errors.

### Upgrade an existing installation

If the target already contains the correct Basil executable:

1. Stop Basil.
2. Replace the executable files with the new version.
3. Preserve the existing `Data/` directory.
4. Preserve or update `appsettings.json` as required.
5. Start Basil.
6. Check the startup log and verify the server is responding.

**Never replace `Data/` with an empty directory during an upgrade.** It contains the database, beatmaps, avatars,
replays, and other persistent state.

If you intentionally want to preserve the target's existing configuration and data, copy only the new application files
and leave `appsettings.json` and `Data/` untouched.

---

## See also

* [`deployment.md`](deployment.md): standard deployment procedure
* [`https.md`](https.md): HTTPS certificates and `Server:CertPath` / `Server:CertPassword`
* [`logging.md`](logging.md): log files and `Logging:MinimumLevel`
* [`docker.md`](docker.md): container configuration and environment variables
