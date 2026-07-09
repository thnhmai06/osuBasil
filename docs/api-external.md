# API cho công cụ giải đấu bên ngoài (host `api.`)

Tài liệu này dành cho người xây dựng công cụ bên ngoài kết nối vào Basil: overlay stream, bracket tracker, admin
panel... Không cần biết gì về codebase Basil để đọc tài liệu này — chỉ cần biết HTTP/WebSocket/JSON.

Xem [`bot-commands.md`](bot-commands.md) cho lệnh chat BasilBot, [`api-client.md`](api-client.md) cho API mà chính
game client osu! gọi (bancho protocol + `osu-*.php`).

## Tổng quan

- **Base URL**: `https://api.<domain>` (`<domain>` là giá trị `Server:Domain` server admin cấu hình — hỏi
  admin server của bạn giá trị này là gì).
- **Định dạng**: toàn bộ endpoint JSON dưới đây dùng key kiểu `camelCase` (`matchId`, không phải `MatchId`).
  **Ngoại lệ**: 3 kênh WebSocket (mục 2) dùng `PascalCase` (`MatchId`) — hai tầng code khác nhau, chưa được
  đồng bộ hoá casing.
- **Không có OAuth, không rate limit.** Đa số endpoint đọc là public (không cần key). Riêng nhóm **Management
  CRUD** (mục 3) bắt buộc header `X-Admin-Key`.
- **Không phải REST API tổng quát v1/v2** — bề mặt hẹp, phục vụ đúng nhu cầu: xem trận đang diễn ra (TRT), tải
  file, và quản trị dữ liệu qua admin key.
- Số/enum (`mode`, `status`, `winCondition`...) hầu hết là **số nguyên thô** (không phải chuỗi tên) trừ khi ghi chú
  khác. Ngày giờ là chuỗi ISO-8601 (`"2026-07-09T12:34:56Z"`). Field `null` được trả về như `null`, không bị ẩn đi.

---

## 1. Tournament Match Report (TRT) — snapshot JSON

Báo cáo trận đấu (map đã chơi, điểm từng round, người thắng...), dựng lại tại thời điểm gọi từ dữ liệu đã lưu (hoặc
trực tiếp từ trận đang diễn ra nếu chưa kết thúc). **Không cần admin key.**

### `GET /multi/{id}`

**Request**

```
GET https://api.basil.example/multi/42
```

Không cần header hay query param nào. `{id}` là `Matches.Id` (ID trận, số nguyên) — lấy ID này từ chat
(`!mp settings` trả `#<id>`), hoặc từ `GET /matches` (mục 3).

**Response — `200 OK`**

```jsonc
{
  "matchId": 42,
  "name": "Bảng A: Team Alpha vs Team Beta",
  "mode": 0,              // GameMode: 0=osu!, 1=Taiko, 2=CTB, 3=Mania
  "winCondition": 0,      // MatchWinConditions
  "teamType": 2,          // MatchTeamTypes
  "hostId": 7,
  "createdAt": "2026-07-09T10:00:00Z",
  "endedAt": null,        // null nếu trận chưa kết thúc
  "isLive": true,         // true = trận đang mở trong bộ nhớ, false = đã đóng, chỉ còn dữ liệu đã lưu
  "currentMapId": 123456, // chỉ có giá trị khi isLive == true, ngược lại null
  "liveSlots": [          // chỉ có khi isLive == true, ngược lại null
    { "slotIndex": 0, "userId": 7, "status": "Ready", "team": "Red", "mods": 0 },
    { "slotIndex": 1, "userId": 12, "status": "NotReady", "team": "Blue", "mods": 64 }
  ],
  "rounds": [
    {
      "roundIndex": 0,
      "beatmapId": 111222,
      "mapMd5": "9f8c2e1a...",
      "mods": 0,
      "startedAt": "2026-07-09T10:05:00Z",
      "endedAt": "2026-07-09T10:09:30Z",
      "winnerUserId": 7,
      "winnerTeam": "Red",
      "scores": [
        {
          "userId": 7,
          "userName": "PlayerOne",
          "team": "Red",
          "mods": 0,
          "score": 987654,
          "acc": 98.42,
          "maxCombo": 512,
          "n300": 480, "n100": 10, "n50": 0, "nMiss": 1,
          "nGeki": 100, "nKatu": 8,
          "grade": "S",
          "perfect": false
        }
      ]
    }
  ]
}
```

Ghi chú:
- `liveSlots[].status`/`team` là **chuỗi tên enum** (`"Ready"`, `"Red"`...) — khác với `mode`/`winCondition`/
  `teamType` ở top-level là số nguyên. Không nhất quán nhưng đúng như code trả về.
- `rounds` luôn có mặt (mảng rỗng nếu chưa round nào chơi xong); `liveSlots`/`currentMapId` biến mất (thành `null`)
  ngay khi trận đóng (`!mp close`, hoặc mất referee cuối cùng).

**Lỗi**: `404` (body rỗng) nếu `{id}` không tồn tại trong `Matches`.

---

## 2. Kênh trực tiếp qua WebSocket

Ba socket độc lập, cùng scope theo `{id}` trận. Cả ba **chỉ đẩy dữ liệu từ server xuống** — không gửi gì lên
socket cả, server bỏ qua/không đọc message client gửi. Payload dùng **`PascalCase`** (khác snapshot JSON ở mục 1).
Client rớt kết nối chậm sẽ bị rớt frame cũ nhất (buffer 32 frame, không chờ).

### 2.1 Kênh chính (main) — `WS /multi/{id}`

Cùng path với snapshot JSON — chỉ khác là bạn upgrade sang WebSocket thay vì `GET` thường. Đẩy mỗi khi slot/map/
trạng thái trận đổi (join/leave/ready/đổi map...), **không có điểm số từng người**.

**Kết nối** (ví dụ JS):

```js
const ws = new WebSocket("wss://api.basil.example/multi/42");
ws.onmessage = (e) => console.log(JSON.parse(e.data));
```

**Payload mỗi frame:**

```jsonc
{
  "MatchId": 42,
  "Name": "Bảng A: Team Alpha vs Team Beta",
  "CurrentMapId": 123456,
  "CurrentMapMd5": "9f8c2e1a...",
  "InProgress": false,
  "WinCondition": 0,
  "TeamType": 2,
  "HostId": 7,
  "Slots": [
    { "SlotIndex": 0, "UserId": 7, "Status": "Ready", "Team": "Red", "Mods": 0 },
    { "SlotIndex": 1, "UserId": 12, "Status": "NotReady", "Team": "Blue", "Mods": 64 }
  ]
}
```

Muốn biết ai thắng round/điểm từng người → gọi lại snapshot JSON (mục 1), kênh này không có.

### 2.2 Kênh điểm số từng player — `WS /multi/{id}/{playerName}`

Đẩy điểm số **trực tiếp trong lúc đang chơi** (score frame gửi lên mỗi vài trăm ms từ client osu! của
`{playerName}`) — dùng cho overlay HUD hiển thị combo/HP realtime, không phải kết quả cuối round (kết quả cuối
nằm trong TRT snapshot sau khi round kết thúc).

```js
const ws = new WebSocket("wss://api.basil.example/multi/42/PlayerOne");
```

```jsonc
{
  "PlayerName": "PlayerOne",
  "Time": 45230,          // mốc thời gian trong bài (ms)
  "Num300": 210, "Num100": 3, "Num50": 0, "NumGeki": 40, "NumKatu": 2, "NumMiss": 0,
  "TotalScore": 456789,
  "MaxCombo": 320,
  "CurrentCombo": 320,
  "Perfect": true,
  "CurrentHp": 195
}
```

Chỉ có dữ liệu khi `{playerName}` thực sự đang trong trận và đang chơi — không tồn tại player/không đang chơi thì
im lặng không có frame nào tới (không lỗi).

### 2.3 Kênh spectator input (raw) — `WS /multi/{id}/input`

Raw replay-frame của **bất kỳ ai đang được spectate** trong trận — dùng để dựng lại chuyển động chuột/phím cho
overlay nâng cao. Chỉ có dữ liệu khi có người thật đang spectate ai đó trong trận.

```jsonc
{
  "PlayerName": "PlayerOne",
  "DataBase64": "eJyz..." // bytes replay-frame gốc, chưa giải mã, base64
}
```

`DataBase64` là dữ liệu thô của osu! client — muốn dùng phải tự giải mã theo định dạng replay frame của osu!
(ngoài phạm vi tài liệu này).

**Lỗi cả 3 socket**: gọi bằng `GET` thường (không upgrade) → `400`. Trận `{id}` không tồn tại → socket vẫn mở
nhưng không có frame nào (không đóng, không báo lỗi).

---

## 3. Tải file — public, không cần admin key

| API | Content-Type | Mô tả |
| --- | --- | --- |
| `GET /replays/{scoreId}` | `application/octet-stream` | Tải file `.osr` (replay) đã lưu theo `scoreId` |
| `GET /beatmaps/{beatmapId}` | `text/plain` | Tải file `.osu` (1 difficulty) đã nạp cục bộ |
| `GET /beatmapsets/{setId}` | `application/x-osu-beatmap-archive` | Đóng gói `.osz` mới ngay lúc gọi, gồm mọi difficulty đã nạp trong set (chỉ file `.osu`, **không** có audio/ảnh nền) |

**Ví dụ:**

```
GET https://api.basil.example/replays/9001
→ 200, Content-Type: application/octet-stream, body: bytes file .osr

GET https://api.basil.example/beatmapsets/5000
→ 200, Content-Type: application/x-osu-beatmap-archive
   Content-Disposition tên file: 5000.osz
```

**Lỗi**: `404` **thân rỗng** (không có JSON `{ error: ... }` nào) nếu không tìm thấy — không tồn tại trong DB,
hoặc DB có nhưng file vật lý không có trên đĩa.

---

## 4. Management CRUD — cần admin key

Toàn bộ endpoint dưới đây yêu cầu header:

```
X-Admin-Key: <giá trị Api__AdminKey do admin server cấu hình>
```

Thiếu header, hoặc sai giá trị → `401`. **Nếu admin chưa cấu hình `Api__AdminKey` thì toàn bộ nhóm này bị khoá
cứng** (không có chế độ "mở nếu chưa set key").

### 4.1 Beatmap

| API | Mô tả |
| --- | --- |
| `GET /beatmaps` | Liệt kê/tìm beatmap |
| `GET /beatmaps/{id}` | Lấy 1 beatmap |
| `POST /beatmaps` | Upload `.osu`/`.osz` |
| `POST /beatmaps/rescan` | Quét lại thư mục lưu trữ, nạp beatmap mới |
| `DELETE /beatmaps/{id}` | Xoá 1 beatmap |

**`GET /beatmaps?query=&mode=&status=&offset=&amount=`**

Mọi query param đều tuỳ chọn: `query` (tìm theo tên/nghệ sĩ), `mode` (số GameMode), `status` (số RankedStatus),
`offset` (mặc định `0`), `amount` (mặc định `50` nếu để `0`).

```
GET https://api.basil.example/beatmaps?query=frontier&amount=10
X-Admin-Key: my-secret-key
```

Response — **mảng của mảng**, nhóm theo beatmap set (mỗi set là 1 mảng con, sắp theo độ khó tăng dần):

```jsonc
[
  [
    {
      "md5": "9f8c2e1a...", "id": 111222, "setId": 5000,
      "artist": "xi", "title": "Frontier", "version": "Insane",
      "creator": "MapperName", "lastUpdate": "2024-01-01T00:00:00Z",
      "totalLength": 120, "maxCombo": 512,
      "status": 2, "frozen": false, "plays": 10, "passes": 4,
      "mode": 0, "bpm": 180.0, "cs": 4.0, "od": 8.0, "ar": 9.0, "hp": 6.0,
      "diff": 5.42, "filename": "xi - Frontier (MapperName) [Insane].osu",
      "fullName": "xi - Frontier [Insane]",
      "hasLeaderboard": true, "hasLeaderboardStrict": true, "awardsRankedScore": true
    }
  ]
]
```

`fullName`/`hasLeaderboard`/`hasLeaderboardStrict`/`awardsRankedScore` là giá trị suy ra (không lưu DB) nhưng vẫn
xuất hiện trong JSON.

**`GET /beatmaps/{id}`** — 1 object `Beatmap` như trên (không bọc mảng). `404` nếu không tồn tại.

**`POST /beatmaps`** — multipart form, field file tên `"file"` (`.osu` hoặc `.osz`, khác thì `400`):

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

Response: `{ "ingested": 4 }` (số beatmap vừa nạp thành công).

**`POST /beatmaps/rescan`** — không cần body. Response: `{ "ingested": 12 }`.

**`DELETE /beatmaps/{id}`** — `204` (thành công, xoá cả hàng DB lẫn file cục bộ), `404` nếu không tồn tại.

### 4.2 User

| API | Mô tả |
| --- | --- |
| `GET /users` | Liệt kê user |
| `GET /users/{id}` | Lấy 1 user |
| `POST /users` | Tạo user |
| `PUT /users/{id}` | Sửa user (partial update) |
| `POST /users/{id}/avatar` | Upload avatar |
| `DELETE /users/{id}` | Xoá (soft-delete) user |

**`User` shape** (dùng chung cho mọi response bên dưới):

```jsonc
{
  "id": 7, "name": "PlayerOne", "safeName": "playerone",
  "email": "player@example.com", "priv": 3, "country": "VN",
  "silenceEnd": 0, "donorEnd": 0,
  "creationTime": 1700000000, "latestActivity": 1720000000,
  "clanId": 0, "clanPriv": 0,
  "preferredMode": 0, "playStyle": 0,
  "customBadgeName": null, "customBadgeIcon": null,
  "userpageContent": null
}
```

> `apiKey` đã bị xoá khỏi `User` — là dead code chưa bao giờ dùng (IRC authentication dùng trực tiếp password osu!).

**`POST /users`** — request body JSON:

```jsonc
{ "name": "PlayerOne", "email": "player@example.com", "password": "plaintext-pass", "country": "VN" }
```

`country` tuỳ chọn. Server tự MD5 rồi bcrypt `password` — gửi mật khẩu thô trong JSON, không tự hash trước.
Response: object `User` vừa tạo.

```
POST https://api.basil.example/users
X-Admin-Key: my-secret-key
Content-Type: application/json

{ "name": "PlayerOne", "email": "player@example.com", "password": "hunter2", "country": "VN" }
```

**`PUT /users/{id}`** — body JSON, mọi field tuỳ chọn (chỉ field có mặt mới bị đổi):

```jsonc
{ "name": "NewName", "country": "US", "priv": 3 }
```

Response: object `User` đã cập nhật. `404` nếu `{id}` không tồn tại.

**`POST /users/{id}/avatar`** — multipart, field `"file"` giống upload beatmap. `204` khi thành công.

**`DELETE /users/{id}`** — soft-delete (đặt `priv = 0`, không xoá hàng DB). `204`, hoặc `404`.

### 4.3 Replay

| API | Mô tả |
| --- | --- |
| `GET /replays` | Liệt kê ID score đang có replay lưu |
| `DELETE /replays/{scoreId}` | Xoá 1 file replay |

`GET /replays` → `[9001, 9002, 9010]` (mảng số nguyên, lấy từ tên file `.osr` trên đĩa, không phải bảng DB
riêng). `DELETE /replays/{scoreId}` → `204`, hoặc `404` nếu file không tồn tại.

### 4.4 Match

| API | Mô tả |
| --- | --- |
| `GET /matches` | Liệt kê mọi trận đã lưu |
| `DELETE /matches/{id}` | Xoá 1 trận (và toàn bộ round/score của nó) |

`GET /matches` → mảng `MatchRow`:

```jsonc
[
  {
    "id": 42, "name": "Bảng A: Team Alpha vs Team Beta",
    "mode": 0, "winCondition": 0, "teamType": 2, "hostId": 7,
    "createdAt": "2026-07-09T10:00:00Z", "endedAt": null
  }
]
```

`DELETE /matches/{id}` → `204` (xoá cascading `Rounds`/`Scores` trong 1 transaction), hoặc `404`.

### 4.5 Ảnh nền theo mùa (seasonal background)

| API | Mô tả |
| --- | --- |
| `GET /seasonals` | Liệt kê tên file đã upload |
| `POST /seasonals` | Upload 1 ảnh nền |
| `DELETE /seasonals/{fileName}` | Xoá 1 ảnh nền |

`GET /seasonals` → `["winter1.jpg", "winter2.jpg"]` (**tên file trần**, không phải URL đầy đủ — khác với
`GET /web/osu-getseasonal.php` bên host `osu.` trả URL đầy đủ, xem [`api-client.md`](api-client.md)).

`POST /seasonals` — multipart, field `"file"`, lưu nguyên tên file gốc (đã lọc ký tự path traversal). `204`.

`DELETE /seasonals/{fileName}` → `204`, hoặc `404`.

---

## Bảng tổng hợp mã lỗi

| Mã | Khi nào | Body |
| --- | --- | --- |
| `400` | Gọi WS endpoint (mục 2) bằng HTTP thường thay vì upgrade | rỗng |
| `401` | Thiếu/sai `X-Admin-Key` trên route Management CRUD | rỗng |
| `404` | Resource không tồn tại (trận, beatmap, user, replay, file...) | **rỗng** — không có JSON `{ error }` ở bất kỳ đâu trong file này |
