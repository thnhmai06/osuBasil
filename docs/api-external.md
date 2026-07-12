# API for external tournament tooling (host `api.`)

This document is for people building external tools that connect to Basil: stream overlays, bracket trackers, admin panels... No knowledge of Basil's codebase is needed to read this document — just HTTP/WebSocket/JSON.

See [`bot-commands.md`](bot-commands.md) for BasilBot chat commands, [`api-client.md`](api-client.md) for the API consumed by the osu! game client itself (bancho protocol + `osu-*.php`).

## Overview

- **Base URL**: `https://api.<domain>` (`<domain>` is the `Server:Domain` value configured by the server admin — ask your server admin for this value).
- **Format**: all JSON endpoints below use `camelCase` keys (`matchId`, not `MatchId`). **Exception**: the 3 WebSocket channels (section 2) use `PascalCase` (`MatchId`) — two different code layers, not yet synchronized.
- **No OAuth, no rate limit.** Most read endpoints are public (no key required). Only the **Management CRUD** group (section 3) requires the `X-Admin-Key` header.
- **Not a general-purpose REST API v1/v2** — narrow surface serving specific needs: viewing live matches (TRT), file downloads, and data management via admin key.
- Numbers/enums (`mode`, `status`, `winCondition`...) are mostly **raw integers** (not name strings) unless noted otherwise. Dates are ISO-8601 strings (`"2026-07-09T12:34:56Z"`). `null` fields are returned as `null`, not omitted.

---

## 1. Tournament Match Report (TRT) — JSON snapshot

Match report (maps played, round scores, winner...), reconstructed at call time from stored data (or directly from the live match if still in progress). **No admin key required.**

### `GET /multi/{id}`

**Request**

```
GET https://api.basil.example/multi/42
```

No headers or query params needed. `{id}` is `Matches.Id` (integer match ID) — get this from chat (`!mp settings` returns `#<id>`), or from `GET /matches` (section 3).

**Response — `200 OK`**

```jsonc
{
  "matchId": 42,
  "name": "Group A: Team Alpha vs Team Beta",
  "createdAt": "2026-07-09T10:00:00Z",
  "endedAt": null,        // null if match not yet ended
  "live": null,           // null if match is closed; object if match is open in memory
  "events": [
    {
      "eventType": 0, "eventTypeName": "Created",
      "actorUserId": null, "actorUserName": null,
      "targetUserId": null, "targetUserName": null,
      "timestamp": "2026-07-09T10:00:00Z", "detail": null
    },
    {
      "eventType": 4, "eventTypeName": "PlayerJoined",
      "actorUserId": null, "actorUserName": null,
      "targetUserId": 7, "targetUserName": "PlayerOne",
      "timestamp": "2026-07-09T10:00:01Z", "detail": null
    }
  ],
  "rounds": [
    {
      "roundIndex": 0,
      "beatmapId": 111222,
      "mapMd5": "9f8c2e1a...",
      "beatmapArtist": "Camellia", "beatmapTitle": "Miku",
      "beatmapVersion": "Another", "beatmapCreator": "MapperName",
      "mode": 0, "winCondition": 0, "teamType": 2,
      "mods": 0, "aborted": false,
      "startedAt": "2026-07-09T10:05:00Z",
      "endedAt": "2026-07-09T10:09:30Z",
      "winnerUserId": 7, "winnerUserName": "PlayerOne",
      "winnerTeam": "Red", "winMetric": "score", "winDiff": 12345,
      "scores": [
        {
          "userId": 7, "userName": "PlayerOne",
          "team": "Red", "mods": 0,
          "score": 987654, "acc": 98.42, "maxCombo": 512,
          "n300": 480, "n100": 10, "n50": 0, "nMiss": 1,
          "nGeki": 100, "nKatu": 8,
          "grade": "S", "perfect": false,
          "submittedAt": "2026-07-09T10:09:30Z"
        }
      ]
    }
  ]
}
```

When `live != null`:

```jsonc
"live": {
  "host": { "userId": 7, "userName": "PlayerOne", "country": "VN" },
  "referees": [
    { "userId": 1, "userName": "RefBot", "country": null }
  ],
  "slots": [
    { "slotIndex": 0, "userId": 7, "userName": "PlayerOne", "country": "VN",
      "status": "Ready", "team": "Red", "mods": 0 },
    { "slotIndex": 1, "userId": null, "userName": null, "country": null,
      "status": "Open", "team": "Neutral", "mods": 0 }
  ],
  "currentMapId": 111222, "currentMapMd5": "9f8c2e1a...",
  "mode": 0, "winCondition": 0, "teamType": 2,
  "mods": 0, "freemods": false, "inProgress": false
}
```

Notes:
- `live.host` is an **object** (`userId`/`userName`/`country`), not a top-level integer `hostId`.
- `live` = `null` as soon as the match closes (`!mp close`, or last referee leaves).
- `events` is an array of lifecycle events: Created(0), RoundStarted(1), RoundEnded(2), HostGranted(3), PlayerJoined(4), PlayerLeft(5), Kicked(6), Closed(7). `actorUserId` = `null` for system events, `!= null` for events triggered by `!mp` commands.
- `rounds[].aborted` = `true` if round was aborted (`!mp abort` or server startup recovery).
- `winMetric` is human-readable: `"score"`, `"accuracy"`, `"combo"`, `"scorev2"`.
- `winDiff`: solo = metric gap between #1 and #2; team = total metric difference between 2 teams; = 0 if draw or match has ≤1 player.
- `scores[].submittedAt` = server receive time (`ServerTime`) — distinct from `startedAt` of the round.
- `rounds` is always present (empty array if no rounds finished yet).

**Errors**: `404` (empty body) if `{id}` does not exist in `Matches`.

---

### 1.2 Match privacy

**`GET /api/multi/{id}/privacy`** — public, no admin key required. Reads the current privacy status of a match.

```
GET https://api.basil.example/multi/42/privacy
```

Response — `200 OK`:
```jsonc
{
  "isPrivate": false
}
```

- Returns the live `IsPrivate` flag for matches still open in memory.
- Closed matches (or matches not currently live) return `isPrivate: false` (the flag is not persisted).
- **Errors**: `404` empty body if `{id}` does not exist in `Matches` at all.

---

## 2. Live channels via WebSocket

Three independent sockets, all scoped to the same match `{id}`. All three **only push data from server to client** — no upload messages are read (server ignores them). Payloads use **`PascalCase`** (unlike the JSON snapshot in section 1). A slow client will have its oldest frames dropped (buffer of 32 frames, no waiting).

### 2.1 Main channel — `WS /multi/{id}`

Same path as the JSON snapshot — just upgrade to WebSocket instead of plain `GET`. Pushes on every slot/map/state change (join/leave/ready/map change...), **no per-player scores**.

**Connection** (JS example):

```js
const ws = new WebSocket("wss://api.basil.example/multi/42");
ws.onmessage = (e) => console.log(JSON.parse(e.data));
```

**Payload per frame:**

```jsonc
{
  "MatchId": 42,
  "Name": "Group A: Team Alpha vs Team Beta",
  "CurrentMapId": 123456,
  "CurrentMapMd5": "9f8c2e1a...",
  "InProgress": false,
  "WinCondition": 0,
  "TeamType": 2,
  "Mode": 0,
  "Mods": 0,
  "Freemods": false,
  "Host": { "UserId": 7, "UserName": "PlayerOne", "Country": "VN" },
  "Referees": [
    { "UserId": 1, "UserName": "RefBot", "Country": null }
  ],
  "Slots": [
    { "SlotIndex": 0, "UserId": 7, "UserName": "PlayerOne", "Country": "VN",
      "Status": "Ready", "Team": "Red", "Mods": 0 },
    { "SlotIndex": 1, "UserId": 12, "UserName": "PlayerTwo", "Country": "US",
      "Status": "NotReady", "Team": "Blue", "Mods": 64 }
  ]
}
```

Notes: `HostId` (integer) was replaced by a `Host` object with `UserName`/`Country`. `Mode`, `Mods`, `Freemods`, `Referees` fields are present. `Slots` includes `UserName`/`Country` instead of just `UserId`.

For round winner/player scores → call the JSON snapshot (section 1); this channel does not have them.

### 2.2 Per-player score channel — `WS /multi/{id}/{playerName}`

Pushes **live scores during gameplay** (score frames sent every few hundred ms by the osu! client of `{playerName}`) — for overlay HUD displaying combo/HP in real time, not the final round result (final results are in the TRT snapshot after the round ends).

```js
const ws = new WebSocket("wss://api.basil.example/multi/42/PlayerOne");
```

```jsonc
{
  "PlayerName": "PlayerOne",
  "Time": 45230,          // time marker within the beatmap (ms)
  "Num300": 210, "Num100": 3, "Num50": 0, "NumGeki": 40, "NumKatu": 2, "NumMiss": 0,
  "TotalScore": 456789,
  "MaxCombo": 320,
  "CurrentCombo": 320,
  "Perfect": true,
  "CurrentHp": 195
}
```

Data only when `{playerName}` is actually in the match and playing — if the player does not exist or is not playing, socket stays silent (no error).

### 2.3 Spectator input channel (raw) — `WS /multi/{id}/input`

Raw replay-frames of **anyone being spectated** in the match — for reconstructing mouse/keyboard movements in advanced overlays. Data only when someone is actually spectating someone else in the match.

```jsonc
{
  "PlayerName": "PlayerOne",
  "DataBase64": "eJyz..." // raw replay-frame bytes, undecoded, base64
}
```

`DataBase64` is raw osu! client data — to use it you must decode it yourself per the osu! replay-frame format (outside the scope of this document).

**Errors for all 3 sockets**: plain `GET` (no upgrade) → `400`. Match `{id}` does not exist → socket still opens but no frames arrive (no close, no error).

---

## 3. File downloads — public, no admin key

| API | Content-Type | Description |
| --- | --- | --- |
| `GET /replays/{scoreId}` | `application/octet-stream` | Download saved `.osr` file by `scoreId` |
| `GET /beatmaps/{beatmapId}` | `text/plain` | Download a single `.osu` difficulty file that has been locally ingested |
| `GET /beatmapsets/{setId}` | `application/x-osu-beatmap-archive` | Package a fresh `.osz` on-the-fly containing all ingested difficulties in the set (`.osu` files only, **no** audio/background) |

**Examples:**

```
GET https://api.basil.example/replays/9001
→ 200, Content-Type: application/octet-stream, body: .osr file bytes

GET https://api.basil.example/beatmapsets/5000
→ 200, Content-Type: application/x-osu-beatmap-archive
   Content-Disposition filename: 5000.osz
```

**Errors**: `404` **empty body** (no JSON `{ error: ... }`) if not found — either not in DB, or DB has it but the physical file is missing on disk.

---

## 4. Management CRUD — admin key required

All endpoints below require the header:

```
X-Admin-Key: <value of Server:AdminKey configured in settings.toml>
```

Missing or wrong header → `401`. **If admin has not configured `Server:AdminKey` in `settings.toml`, the entire group is locked hard** (no "open when key unset" mode).

### 4.1 Beatmap

| API | Description |
| --- | --- |
| `GET /beatmaps` | List/find beatmaps |
| `GET /beatmaps/{id}` | Get one beatmap |
| `POST /beatmaps` | Upload `.osu`/`.osz` |
| `POST /beatmaps/rescan` | Rescan the storage directory, ingest new beatmaps |
| `DELETE /beatmaps/{id}` | Delete one beatmap |

**`GET /beatmaps?query=&mode=&status=&offset=&amount=`**

All query params optional: `query` (search by name/artist), `mode` (GameMode number), `status` (RankedStatus number), `offset` (default `0`), `amount` (default `50` if `0`).

```
GET https://api.basil.example/beatmaps?query=frontier&amount=10
X-Admin-Key: my-secret-key
```

Response — **array of arrays**, grouped by beatmap set (each set = 1 sub-array, sorted by difficulty ascending):

```jsonc
[
  [
    {
      "md5": "9f8c2e1a...", "id": 111222, "setId": 5000,
      "artist": "Camellia", "title": "Frontier", "version": "Insane",
      "creator": "MapperName", "lastUpdate": "2024-01-01T00:00:00Z",
      "totalLength": 120, "maxCombo": 512,
      "status": 2, "frozen": false, "plays": 10, "passes": 4,
      "mode": 0, "bpm": 180.0, "cs": 4.0, "od": 8.0, "ar": 9.0, "hp": 6.0,
      "diff": 5.42, "filename": "Camellia - Frontier (MapperName) [Insane].osu",
      "fullName": "Camellia - Frontier [Insane]",
      "hasLeaderboard": true, "hasLeaderboardStrict": true, "awardsRankedScore": true
    }
  ]
]
```

`fullName`/`hasLeaderboard`/`hasLeaderboardStrict`/`awardsRankedScore` are derived values (not stored in DB) but still present in JSON.

**`GET /beatmaps/{id}`** — 1 `Beatmap` object (not wrapped in array). `404` if not found.

**`POST /beatmaps`** — multipart form, file field named `"file"` (`.osu` or `.osz`, anything else → `400`):

```
POST https://api.basil.example/beatmaps
X-Admin-Key: my-secret-key
Content-Type: multipart/form-data; boundary=...

--...
Content-Disposition: form-data; name="file"; filename="mapset.osz"
Content-Type: application/octet-stream

<bytes>
--...--
```

Response: `{ "ingested": 4 }` (number of beatmaps successfully ingested).

**`POST /beatmaps/rescan`** — no body needed. Response: `{ "ingested": 12 }`.

**`DELETE /beatmaps/{id}`** — `204` (success, deletes both DB row and local file), `404` if not found.

### 4.2 User

| API | Description |
| --- | --- |
| `GET /users` | List users |
| `GET /users/{id}` | Get one user |
| `POST /users` | Create user |
| `PUT /users/{id}` | Update user (partial update) |
| `POST /users/{id}/avatar` | Upload avatar |
| `DELETE /users/{id}` | Delete (soft-delete) user |

**`User` shape** (used for all responses below):

```jsonc
{
  "id": 7, "name": "PlayerOne", "safeName": "playerone",
  "priv": 19, "country": "VN",
  "silenceEnd": 0, "donorEnd": 0,
  "creationTime": 1700000000, "latestActivity": 1720000000,
  "clanId": 0, "clanPriv": 0,
  "preferredMode": 0, "playStyle": 0,
  "customBadgeName": null, "customBadgeIcon": null,
  "userpageContent": null
}
```

> `apiKey` was removed from `User` — was dead code never used (IRC authentication uses the osu! password directly). `email` was removed — the field no longer exists in the schema.

**`POST /users`** — JSON request body:

```jsonc
{ "name": "PlayerOne", "password": "plaintext-pass", "country": "VN", "priv": 19 }
```

- `country` is optional (default `"xx"`).
- `priv` is optional (default `19` = `Unrestricted | Verified | Supporter` — see [`privileges.md`](privileges.md)).
- Server MD5-hashes then bcrypts the `password` — send raw password in JSON, do not pre-hash.
- Response: created `User` object.

```
POST https://api.basil.example/users
X-Admin-Key: my-secret-key
Content-Type: application/json

{ "name": "PlayerOne", "password": "hunter2", "country": "VN" }
```

**`PUT /users/{id}`** — JSON body, all fields optional (only present fields are changed):

```jsonc
{ "name": "NewName", "country": "US", "priv": 3 }
```

Response: updated `User` object. `404` if `{id}` does not exist.

**`POST /users/{id}/avatar`** — multipart, field `"file"` (same as beatmap upload). `204` on success.

**`DELETE /users/{id}`** — soft-delete (sets `priv = 0`, does not delete DB row). `204`, or `404`.

### 4.3 Replay

| API | Description |
| --- | --- |
| `GET /replays` | List score IDs that have stored replays |
| `DELETE /replays/{scoreId}` | Delete one replay file |

`GET /replays` → `[9001, 9002, 9010]` (array of integers, read from `.osr` filenames on disk, not from a separate DB table). `DELETE /replays/{scoreId}` → `204`, or `404` if file does not exist.

### 4.4 Match

| API | Description |
| --- | --- |
| `GET /matches` | List all stored matches |
| `DELETE /matches/{id}` | Delete one match (and all its rounds/scores) |
| `PUT /multi/{id}/privacy` | Set a live match's private status (`X-Admin-Key` required) |

`GET /matches` → array:

```jsonc
[
  {
    "id": 42, "name": "Group A: Team Alpha vs Team Beta",
    "createdAt": "2026-07-09T10:00:00Z", "endedAt": null
  }
]
```

`PUT /multi/{id}/privacy` — request body:
```jsonc
{ "isPrivate": true }
```

- Response `200`: `{ "isPrivate": true }`
- Response `400`: invalid body
- Response `401`: missing/wrong `X-Admin-Key`
- Response `404`: match `{id}` not found or not currently live
- Broadcasts the change to the match's channel and (if becoming public) to `#lobby`.

`DELETE /matches/{id}` → `204` (cascading delete of `Rounds`/`Scores` in one transaction), or `404`.

### 4.5 Seasonal background

| API | Description |
| --- | --- |
| `GET /seasonals` | List uploaded filenames |
| `POST /seasonals` | Upload a background image |
| `DELETE /seasonals/{fileName}` | Delete a background image |

`GET /seasonals` → `["winter1.jpg", "winter2.jpg"]` (**bare filenames**, not full URLs — unlike `GET /web/osu-getseasonal.php` on the `osu.` host, which returns full URLs, see [`api-client.md`](api-client.md)).

`POST /seasonals` — multipart, field `"file"`, saved with original filename (path-traversal characters filtered). `204`.

`DELETE /seasonals/{fileName}` → `204`, or `404`.

---

## Error code summary

| Code | When | Body |
| --- | --- | --- |
| `400` | Calling a WS endpoint (section 2) with plain HTTP instead of upgrade | empty |
| `401` | Missing/wrong `X-Admin-Key` on Management CRUD routes | empty |
| `404` | Resource not found (match, beatmap, user, replay, file...) | **empty** — no JSON `{ error }` anywhere in this file |
