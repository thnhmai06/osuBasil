# API tương tác với osu! client

Mọi thứ mà game client osu! thật giao tiếp: packet bancho qua kết nối nhị phân bền vững (persistent), và các HTTP
endpoint kiểu `/web/osu-*.php` mà nó gọi song song với kết nối đó. Để biết cái gì là stub, cái gì thật, và tại sao,
xem [`scope-decisions.md`](scope-decisions.md).

Cột **"osu! Bancho bình thường"** so sánh với **server osu! Bancho chính thức** (osu.ppy.sh) — không phải bancho.py
(fork mã nguồn mở mà dự án này port kiến trúc từ đó, xem [`CLAUDE.md`](../CLAUDE.md)). Bản thân wire protocol
(packet ID, thứ tự field) do chính client osu! thật định nghĩa nên hầu hết các packet dưới đây giống hệt dù so với
bancho.py hay Bancho chính thức; khác biệt đáng kể nằm ở tầng chat-command (BanchoBot) và việc Bancho chính thức có
lưu match history đầy đủ dạng trang web xem lại được. Cột **"So sánh"**:

- ✅ Có - hành vi khớp Bancho chính thức
- ⚠️ Có nhưng có giới hạn - hành vi khác nhau, hoặc Basil mở rộng thêm so với Bancho chính thức
- ❌ Không - không tồn tại trên Bancho chính thức

Xem [`bot-commands.md`](bot-commands.md) cho wiki lệnh chat BasilBot, [`api-external.md`](api-external.md) cho API
`api.` host expose cho công cụ giải đấu bên ngoài (JSON/WebSocket, dễ tích hợp hơn nhiều so với các endpoint ở
đây — nếu bạn đang xây overlay/bracket tracker chứ không phải client osu! thật, đọc file đó thay vì file này).

## Endpoint được host ở đâu

Mọi endpoint bên dưới — bancho protocol, osu-web, beatmap asset — đều là một app ASP.NET Core duy nhất
(`Basil.Web`), được route tách biệt bằng **hostname, không phải path prefix**. `BanchoHostGroups.MapAll`
(`src/Basil.Web/Routing/BanchoHostGroups.cs:33`) xây dựng danh sách host thực tế khi khởi động từ một giá trị
config duy nhất:

```
ServerBehavior:Domain      (appsettings.json)
ServerBehavior__Domain     (biến môi trường — override khi chạy executable đã publish)
```

Với domain đã cấu hình đó **và** `ppy.sh` được hardcode (để một deployment production đứng sau reverse proxy trả
lời trên domain osu! thật hoạt động mà không cần config thêm), nó sinh ra:

| Nhóm host                                            | Hostname                                                                 | Route group            |
|------------------------------------------------------|--------------------------------------------------------------------------|------------------------|
| bancho protocol                                      | `c.<domain>`, `ce.<domain>`, `c4.<domain>`, `c5.<domain>`, `c6.<domain>` | `MapBanchoGroup`       |
| osu-web                                              | `osu.<domain>`                                                           | `MapOsuWebGroup`       |
| asset thumbnail/preview beatmap (redirect mirror cũ) | `b.<domain>`                                                             | `MapBeatmapAssetGroup` |
| avatar (cục bộ, offline pivot)                       | `a.<domain>`                                                             | `MapAvatarGroup`       |

`Program.cs:20` đọc `ServerBehavior:Domain` và truyền nó vào `MapAll` — đó là nơi duy nhất domain được truyền qua;
không có config host theo từng route nào khác ở bất kỳ đâu.

**Giá trị config đó tự nó nằm ở đâu, theo từng môi trường:**

| Môi trường                               | `ServerBehavior:Domain` (và mọi thứ khác) được set ở đâu                                                                                                       |
|------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `dotnet run` (dev cục bộ)                | `src/Basil.Web/appsettings.Development.json` → `ServerBehavior:Domain` (mặc định `basil.local`)                                                                |
| Executable đã publish                    | biến môi trường `ServerBehavior__Domain` (mặc định `localhost` nếu không set), xem [`run-deployment.md`](run-deployment.md)                                    |

**Network binding (app thực sự lắng nghe port/protocol nào) là một trục config riêng biệt, không liên quan:**

- Dev cục bộ: `Kestrel:Certificates:Default:Path`/`Password` dưới dạng biến môi trường (cert HTTPS để test với
  client thật, xem [`run-deployment.md`](run-deployment.md)), kết hợp với `--urls` truyền vào `dotnet run` (ví
  dụ `--urls "http://*:80;https://*:443"`).
- Executable đã publish: mặc định Kestrel lắng nghe HTTP thuần trên port do ASP.NET Core chọn (override qua
  `--urls` hoặc `ASPNETCORE_URLS`) — dành cho việc đứng sau một reverse proxy thật (nginx/Caddy) terminate TLS và
  forward `X-Forwarded-For`, hoặc set trực tiếp cert như trên nếu chạy độc lập.

---

## 1. Bancho protocol (host `c.`/`ce.`/`c4.`/`c5.`/`c6.`)

Đây là kết nối nhị phân bền vững (persistent) mà client osu! mở khi bạn bấm "đăng nhập". Toàn bộ giao tiếp — cả
login lẫn mọi packet sau đó — đều là `POST /` trên host này; **không có path nào khác**. Điểm khác biệt giữa "đang
login" và "đã login, đang gửi packet" chỉ là 1 header: `osu-token`.

### Cách hoạt động (dạng tổng quát)

```
┌─────────────┐                                  ┌──────────────┐
│ osu! client │──── POST / (không header) ──────▶│ Basil server │
│             │◀─── header cho-token + packet ────│              │
│             │        ban đầu (bantô ID, ...)    │              │
│             │                                    │              │
│             │──── POST / (osu-token: <token>) ─▶│              │  (mỗi vài trăm ms,
│             │◀─── các packet server tích luỹ ────│              │   lặp lại)
│             │        cho session đó              │              │
└─────────────┘                                  └──────────────┘
```

1. **Login (request đầu tiên, không có `osu-token`)** — body request là dạng text nhiều dòng do client tự soạn
   (`username\npassword_md5\nosu_version|utc_offset|display_city|client_hashes|pm_private`). Server (`OsuLoginUseCase`)
   xác thực và trả về:
    - Header `cho-token: <token ngẫu nhiên>` — client phải gửi lại header này ở **mọi** request tiếp theo.
    - Body: chuỗi packet nhị phân ban đầu (bantô ID của bạn, danh sách channel, presence của những người online...).
2. **Mọi request sau đó (có header `osu-token: <token>`)** — body request là **1 hoặc nhiều packet nhị phân client
   gửi lên** (đổi map, gửi chat, ready...), đọc tuần tự bởi `BanchoPacketDispatcher.DispatchAsync`, mỗi packet
   được route tới handler tương ứng (bảng bên dưới) theo packet ID đọc từ header của từng packet trong body.
   Response body là **mọi packet outgoing đã tích luỹ cho session đó** kể từ lần poll trước (chat mới, presence đổi
   của người khác, cập nhật trận...) — nếu chưa có gì mới, response rỗng. Đây chính là cơ chế polling client dùng
   để "nhận" sự kiện real-time, dù chạy trên HTTP thường (không phải WebSocket).
3. **Token không xác định** (ví dụ server vừa restart) → response là thông báo "Server has restarted" kèm packet
   yêu cầu client tự động reconnect (relogin).

Mỗi packet — cả chiều lên lẫn chiều xuống — có cấu trúc nhị phân chung
`[packet ID: u16][padding: u8][length: u32][payload: length bytes]`; payload tuỳ theo từng loại packet mà có field khác
nhau. Định dạng payload chi tiết theo từng packet không nằm trong tài liệu này — 42 loại packet bên dưới liệt kê *
*packet nào tồn tại và làm gì**, không phải layout byte-by-byte (đó là phần do chính client osu! định nghĩa, không phải
Basil).

### 1.1 Core (vòng đời session)

| Packet                   | Handler                         | Mô tả                                                          | osu! Bancho bình thường | So sánh |
|--------------------------|---------------------------------|----------------------------------------------------------------|-------------------------|---------|
| `Ping`                   | `PingHandler`                   | keepalive, phản hồi no-op                                      | Có, hành vi giống hệt   | ✅ Có    |
| `ChangeAction`           | `ChangeActionHandler`           | cập nhật status của chính mình (action/mode/map/mods)          | Có, hành vi giống hệt   | ✅ Có    |
| `RequestStatusUpdate`    | `RequestStatusUpdateHandler`    | client yêu cầu server gửi lại stats của chính nó               | Có, hành vi giống hệt   | ✅ Có    |
| `UserStatsRequest`       | `UserStatsRequestHandler`       | yêu cầu stats cho một tập user ID cụ thể                       | Có, hành vi giống hệt   | ✅ Có    |
| `UserPresenceRequest`    | `UserPresenceRequestHandler`    | yêu cầu presence cho một tập user ID cụ thể                    | Có, hành vi giống hệt   | ✅ Có    |
| `UserPresenceRequestAll` | `UserPresenceRequestAllHandler` | yêu cầu presence cho mọi player đang online                    | Có, hành vi giống hệt   | ✅ Có    |
| `ReceiveUpdates`         | `ReceiveUpdatesHandler`         | set bộ lọc presence (all/friends/none)                         | Có, hành vi giống hệt   | ✅ Có    |
| `SetAwayMessage`         | `SetAwayMessageHandler`         | set/xoá away message hiển thị cho người khác                   | Có, hành vi giống hệt   | ✅ Có    |
| `Logout`                 | `LogoutHandler`                 | ngắt kết nối sạch: rời channel/match, thông báo cho người khác | Có, hành vi giống hệt   | ✅ Có    |

### 1.2 Channels (chat)

| Packet                    | Handler                          | Mô tả                                                                                                                                                                                                                                                                           | osu! Bancho bình thường                                                                                                                                                                                     | So sánh                 |
|---------------------------|----------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-------------------------|
| `ChannelJoin`             | `ChannelJoinHandler`             | tham gia một chat channel                                                                                                                                                                                                                                                       | Có, hành vi giống hệt                                                                                                                                                                                       | ✅ Có                    |
| `ChannelPart`             | `ChannelPartHandler`             | rời một chat channel                                                                                                                                                                                                                                                            | Có, hành vi giống hệt                                                                                                                                                                                       | ✅ Có                    |
| `SendPublicMessage`       | `SendPublicMessageHandler`       | gửi một message tới channel — một message có tiền tố `ServerBehavior:CommandPrefix` (`!help`, `!roll`, `!mp ...`) cũng được broadcast như chat thường, sau đó chuyển cho `ICommandDispatcher`; một reply khác null được broadcast từ danh tính của BasilBot tới cùng channel đó | Có BanchoBot với cùng cơ chế dispatch lệnh chat, nhưng bộ lệnh đầy đủ hơn nhiều (`stats`, `report`, mappool, scrim...) — xem [`bot-commands.md`](bot-commands.md) để biết chính xác lệnh nào Basil có/thiếu | ⚠️ Có nhưng có giới hạn |
| `SendPrivateMessage`      | `SendPrivateMessageHandler`      | gửi DM tới player khác — một DM gửi cho BasilBot đi thẳng tới `ICommandDispatcher` thay vì đường deliver/mail thông thường (ngoại lệ duy nhất: `!mp make`/`makeprivate`, hai lệnh duy nhất dùng được qua DM vì chưa có match nào để làm scope)                                  | Có, BanchoBot nhận lệnh qua DM giống hệt — cùng khác biệt bộ lệnh như trên                                                                                                                                  | ⚠️ Có nhưng có giới hạn |
| `ToggleBlockNonFriendDms` | `ToggleBlockNonFriendDmsHandler` | bật/tắt việc người không phải friend có thể DM bạn                                                                                                                                                                                                                              | Có, hành vi giống hệt                                                                                                                                                                                       | ✅ Có                    |

### 1.3 Spectating

| Packet            | Handler                  | Mô tả                                           | osu! Bancho bình thường                                                                                                                                                                        | So sánh                 |
|-------------------|--------------------------|-------------------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-------------------------|
| `StartSpectating` | `StartSpectatingHandler` | bắt đầu spectate một player                     | Có, hành vi giống hệt                                                                                                                                                                          | ✅ Có                    |
| `StopSpectating`  | `StopSpectatingHandler`  | dừng spectate                                   | Có, hành vi giống hệt                                                                                                                                                                          | ✅ Có                    |
| `SpectateFrames`  | `SpectateFramesHandler`  | chuyển tiếp replay frame tới các spectator khác | Không feed vào kênh WebSocket nào — Basil cũng đẩy dữ liệu này vào `/multi/{id}/input` của host `api.` (xem [`api-external.md`](api-external.md#23-kênh-spectator-input-raw--ws-multiidinput)) | ⚠️ Có nhưng có giới hạn |
| `CantSpectate`    | `CantSpectateHandler`    | báo cho host biết spectator không có map        | Có, hành vi giống hệt                                                                                                                                                                          | ✅ Có                    |

### 1.4 Multiplayer

| Packet                        | Handler                           | Mô tả                                                                                                                                                                                                                 | osu! Bancho bình thường                                                                                                                                                                                           | So sánh                 |
|-------------------------------|-----------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-------------------------|
| `CreateMatch`                 | `CreateMatchHandler`              | tạo một match, tham gia với vai trò host **và** tự động được thêm làm referee (`MatchSession.AddReferee`) — cần thiết để người tạo có quyền dùng `!mp` trên chính trận họ vừa tạo, giống hệt bootstrap của `!mp make` | Có lưu match history đầy đủ, xem lại được dạng trang web (`osu.ppy.sh/mp/{id}`) — Basil chỉ lưu đủ dữ liệu để dựng lại report nội bộ (TRT), không có trang lịch sử public tương đương. Không có khái niệm referee | ⚠️ Có nhưng có giới hạn |
| `JoinMatch`                   | `JoinMatchHandler`                | tham gia một match đã tồn tại                                                                                                                                                                                         | Có, hành vi giống hệt                                                                                                                                                                                             | ✅ Có                    |
| `PartMatch`                   | `PartMatchHandler`                | rời match hiện tại                                                                                                                                                                                                    | Có, hành vi giống hệt                                                                                                                                                                                             | ✅ Có                    |
| `MatchChangeSlot`             | `MatchChangeSlotHandler`          | chuyển sang slot khác                                                                                                                                                                                                 | Có, hành vi giống hệt                                                                                                                                                                                             | ✅ Có                    |
| `MatchChangeSettings`         | `MatchChangeSettingsHandler`      | đổi map/mode/win-condition/team-type                                                                                                                                                                                  | Có, hành vi giống hệt                                                                                                                                                                                             | ✅ Có                    |
| `MatchChangePassword`         | `MatchChangePasswordHandler`      | đổi mật khẩu match                                                                                                                                                                                                    | Có, hành vi giống hệt                                                                                                                                                                                             | ✅ Có                    |
| `MatchChangeMods`             | `MatchChangeModsHandler`          | đổi mods của bản thân hoặc toàn match                                                                                                                                                                                 | Có, hành vi giống hệt                                                                                                                                                                                             | ✅ Có                    |
| `MatchChangeTeam`             | `MatchChangeTeamHandler`          | đổi team (chỉ ở team mode)                                                                                                                                                                                            | Có, hành vi giống hệt                                                                                                                                                                                             | ✅ Có                    |
| `MatchLock`                   | `MatchLockHandler`                | khoá/mở khoá một slot (chỉ host)                                                                                                                                                                                      | Có, hành vi giống hệt                                                                                                                                                                                             | ✅ Có                    |
| `MatchTransferHost`           | `MatchTransferHostHandler`        | chuyển host sang slot khác                                                                                                                                                                                            | Có, hành vi giống hệt                                                                                                                                                                                             | ✅ Có                    |
| `MatchReady`                  | `MatchReadyHandler`               | đánh dấu bản thân ready                                                                                                                                                                                               | Có, hành vi giống hệt                                                                                                                                                                                             | ✅ Có                    |
| `MatchNotReady`               | `MatchNotReadyHandler`            | đánh dấu bản thân chưa ready                                                                                                                                                                                          | Có, hành vi giống hệt                                                                                                                                                                                             | ✅ Có                    |
| `MatchStart`                  | `MatchStartHandler`               | bắt đầu match (chỉ host)                                                                                                                                                                                              | Có, ghi nhận vào match history — Basil mở một hàng `Rounds` riêng để dựng report nội bộ (TRT), không phải trang lịch sử public                                                                                    | ⚠️ Có nhưng có giới hạn |
| `MatchLoadComplete`           | `MatchLoadCompleteHandler`        | báo hiệu load gameplay xong                                                                                                                                                                                           | Có, hành vi giống hệt                                                                                                                                                                                             | ✅ Có                    |
| `MatchSkipRequest`            | `MatchSkipRequestHandler`         | yêu cầu skip đoạn intro                                                                                                                                                                                               | Có, hành vi giống hệt                                                                                                                                                                                             | ✅ Có                    |
| `MatchNoBeatmap`              | `MatchNoBeatmapHandler`           | báo hiệu thiếu beatmap                                                                                                                                                                                                | Có, hành vi giống hệt                                                                                                                                                                                             | ✅ Có                    |
| `MatchHasBeatmap`             | `MatchHasBeatmapHandler`          | báo hiệu beatmap đã có sẵn                                                                                                                                                                                            | Có, hành vi giống hệt                                                                                                                                                                                             | ✅ Có                    |
| `MatchFailed`                 | `MatchFailedHandler`              | báo hiệu fail trong lúc chơi                                                                                                                                                                                          | Có, hành vi giống hệt                                                                                                                                                                                             | ✅ Có                    |
| `MatchScoreUpdate`            | `MatchScoreUpdateHandler`         | chuyển tiếp cập nhật score-frame trực tiếp                                                                                                                                                                            | Không feed vào kênh WebSocket nào — Basil cũng đẩy dữ liệu này vào `/multi/{id}/{playerName}` của host `api.` (xem [`api-external.md`](api-external.md#22-kênh-điểm-số-từng-player--ws-multiidplayername))        | ⚠️ Có nhưng có giới hạn |
| `MatchComplete`               | `MatchCompleteHandler`            | báo hiệu gameplay đã xong cho slot này                                                                                                                                                                                | Có, cập nhật vào match history — Basil đóng hàng `Rounds` tương ứng để dựng report nội bộ (TRT)                                                                                                                   | ⚠️ Có nhưng có giới hạn |
| `MatchInvite`                 | `MatchInviteHandler`              | mời player khác vào match                                                                                                                                                                                             | Có, hành vi giống hệt                                                                                                                                                                                             | ✅ Có                    |
| `TournamentMatchInfoRequest`  | `TourneyMatchInfoRequestHandler`  | tourney client: yêu cầu thông tin match mà không tham gia                                                                                                                                                             | Có, hành vi giống hệt                                                                                                                                                                                             | ✅ Có                    |
| `TournamentJoinMatchChannel`  | `TourneyMatchJoinChannelHandler`  | tourney client: chỉ tham gia chat channel của match                                                                                                                                                                   | Có, hành vi giống hệt                                                                                                                                                                                             | ✅ Có                    |
| `TournamentLeaveMatchChannel` | `TourneyMatchLeaveChannelHandler` | tourney client: rời chat channel của match                                                                                                                                                                            | Có, hành vi giống hệt                                                                                                                                                                                             | ✅ Có                    |

---

## 2. HTTP endpoint (`osu.`/`b.`/`a.`)

Mọi route được đăng ký ở một chỗ duy nhất, `src/Basil.Web/Routing/BanchoHostGroups.cs` — routing theo subdomain
chọn nhóm nào xử lý request (không phải path prefix). **Toàn bộ endpoint dưới đây trả về text/html theo định dạng
wire cũ của osu! (pipe `|`/newline-delimited), không phải JSON** — ngoại lệ duy nhất là `POST /difficulty-rating`.
Cột "Xử lý bởi" (gộp vào "Cách dùng") trỏ tới class thực sự làm việc; "inline" nghĩa là logic nằm trực tiếp trong
route lambda của file đó, không có class service riêng.

### 2.1 `c.`/`ce.`/`c4.`/`c5.`/`c6.` — bancho protocol

Kết nối realtime bền vững của client. Mọi request đều là `POST /` — không có path nào khác trên host group này.
Chi tiết cơ chế và bảng packet ở mục 1.

| API              | Cách dùng                                                                                                                                    | Mô tả                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 | osu! Bancho bình thường | So sánh |
|------------------|----------------------------------------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-------------------------|---------|
| Bancho over HTTP | `POST /` — `MapBanchoGroup` (inline) → `OsuLoginUseCase` (không có header `osu-token`) hoặc `BanchoPacketDispatcher` (có header `osu-token`) | Không có token: coi request body là một lần thử login và chuyển cho `OsuLoginUseCase`, hàm này trả về header `cho-token` cộng stream packet ban đầu. Có token: tra session theo token đó, nếu tìm thấy, forward raw body tới `BanchoPacketDispatcher.DispatchAsync`, hàm này đọc packet ID và gọi handler tương ứng từ bảng packet bancho ở trên; các packet outgoing tích luỹ cho session đó sau đó được flush ra làm response body. Một token không xác định (server đã restart kể từ request cuối của client) nhận thông báo "Server has restarted" + packet reconnect thay vào đó | Có, hành vi giống hệt   | ✅ Có    |

### 2.2 `osu.` — endpoint osu!-web (`/web/*.php`, `/d/*`, `/users`, `/difficulty-rating`)

Mọi thứ mà tầng web-request của client gọi bên ngoài kết nối bancho bền vững — tra cứu beatmap, nộp điểm, tải
replay, và một số stub cố tình không làm gì.

#### Bảng tổng hợp

| API                        | Cách dùng                                                                                  | Mô tả                                                                                                                                                                                                                                                                                                                                                                                                                                                         | osu! Bancho bình thường                                                                                                                     | So sánh                 |
|----------------------------|--------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------|-------------------------|
| Placeholder host           | `POST /` — inline                                                                          | response placeholder `"osu"` cho một request trần tới host — không phải endpoint thật client gọi                                                                                                                                                                                                                                                                                                                                                              | Có, hành vi giống hệt                                                                                                                       | ✅ Có                    |
| Leaderboard                | `GET /web/osu-osz2-getscores.php` — `BeatmapLeaderboardService`                            | lấy leaderboard cho một beatmap (danh sách score, ranked status), giới hạn theo loại leaderboard (global/country/friends/mods)                                                                                                                                                                                                                                                                                                                                | Có, hành vi giống hệt                                                                                                                       | ✅ Có                    |
| Tìm beatmap                | `GET /web/osu-search.php` — `DirectSearchService`                                          | tìm beatmap từ panel "osu!direct" trong game; query bảng `maps` cục bộ, không phụ thuộc mirror/internet                                                                                                                                                                                                                                                                                                                                                       | Có thể query một mirror thật; Basil chỉ cục bộ                                                                                              | ⚠️ Có nhưng có giới hạn |
| Tra beatmap set            | `GET /web/osu-search-set.php` — `IMapRepository.FetchOneAsync` (inline)                    | tra một beatmap set duy nhất theo set ID, map ID, hoặc checksum (chỉ đúng một giá trị được kỳ vọng mỗi request)                                                                                                                                                                                                                                                                                                                                               | Có, hành vi giống hệt                                                                                                                       | ✅ Có                    |
| Tải beatmap set            | `GET /d/{mapSetId}` — inline                                                               | tải beatmap set — nếu có beatmap nào trong set đã được nạp cục bộ, đóng gói một `.osz` mới ngay lúc đó từ các file `{id}.osu` đã lưu (xem `BeatmapIngestionService`); chỉ fallback về một mirror đã cấu hình (`MirrorOptions.DownloadEndpoint`) khi không có gì cục bộ, và báo download không khả dụng nếu cả hai đều không áp dụng. Một `.osz` đóng gói cục bộ chỉ bao giờ chứa file difficulty `.osu` — server này không bao giờ lưu asset audio/background | Luôn phục vụ/redirect một `.osz` thật đầy đủ                                                                                                | ⚠️ Có nhưng có giới hạn |
| Tải file `.osu` lẻ         | `GET /web/maps/{mapFilename}` — inline                                                     | tra filename trong `Beatmaps`, sau đó phục vụ file `{id}.osu` tương ứng từ `Storage__MapsetsPath` — 404 nếu thiếu hàng DB hoặc file (không có fallback tới osu.ppy.sh thật)                                                                                                                                                                                                                                                                                   | Fallback về một redirect osu.ppy.sh thật                                                                                                    | ⚠️ Có nhưng có giới hạn |
| Nộp điểm                   | `POST /web/osu-submit-modular-selector.php` — `IScoreDecryptor` + `ScoreSubmissionUseCase` | endpoint nộp điểm thực sự: giải mã score payload của client, trích replay file từ multipart form nếu có, sau đó chạy toàn bộ pipeline submission (validation, persistence, format response)                                                                                                                                                                                                                                                                   | Cập nhật stats singleplayer/leaderboard-rank khi submit — Basil không bao giờ làm việc này (xem [`scope-decisions.md`](scope-decisions.md)) | ⚠️ Có nhưng có giới hạn |
| Tải replay                 | `GET /web/osu-getreplay.php` — `ReplayService`                                             | lấy một replay file đã lưu theo score ID, để xem replay trong game                                                                                                                                                                                                                                                                                                                                                                                            | Có, hành vi giống hệt                                                                                                                       | ✅ Có                    |
| Info beatmap song-select   | `POST /web/osu-getbeatmapinfo.php` — `BeatmapInfoService`                                  | cho một danh sách filename `.osu`, trả về ranked status từng map và grade của player yêu cầu trên mỗi map (cho overlay song-select)                                                                                                                                                                                                                                                                                                                           | Có, hành vi giống hệt                                                                                                                       | ✅ Có                    |
| Anticheat flag             | `GET /web/lastfm.php` — `ClientIntegrityService`                                           | tiếp nhận cờ anticheat — log một cờ client-hash đáng ngờ (xem [`scope-decisions.md`](scope-decisions.md)); không bao giờ restrict hay kick                                                                                                                                                                                                                                                                                                                    | Có thể flag/restrict/kick thật sự                                                                                                           | ⚠️ Có nhưng có giới hạn |
| Đánh dấu mail đã đọc       | `GET /web/osu-markasread.php` — `MailReadService`                                          | đánh dấu một cuộc hội thoại mail trong game với player khác là đã đọc                                                                                                                                                                                                                                                                                                                                                                                         | Có, hành vi giống hệt                                                                                                                       | ✅ Có                    |
| Danh sách ảnh nền theo mùa | `GET /web/osu-getseasonal.php` — inline                                                    | liệt kê URL ảnh (`osu.<domain>/seasonal/{file}`) cho mọi file trong `Storage__SeasonalsPath`                                                                                                                                                                                                                                                                                                                                                                  | Giống về chức năng, khác nguồn ảnh (cục bộ, không phải osu.ppy.sh)                                                                          | ⚠️ Có nhưng có giới hạn |
| Phục vụ ảnh nền theo mùa   | `GET /seasonal/{fileName}` — inline                                                        | phục vụ một file ảnh nền theo mùa theo tên                                                                                                                                                                                                                                                                                                                                                                                                                    | Không tồn tại — route hậu thuẫn riêng của Basil cho endpoint ở trên                                                                         | ❌ Không                 |
| Bancho connect stub        | `GET /web/bancho_connect.php` — inline stub                                                | response rỗng; không cần auth theo thiết kế (có thể gọi trước khi session tồn tại)                                                                                                                                                                                                                                                                                                                                                                            | Có, hành vi giống hệt                                                                                                                       | ✅ Có                    |
| Check update stub          | `GET /web/check-updates.php` — inline stub                                                 | response rỗng                                                                                                                                                                                                                                                                                                                                                                                                                                                 | Có thể báo thông tin update thật                                                                                                            | ⚠️ Có nhưng có giới hạn |
| Screenshot stub            | `POST /web/osu-screenshot.php` — inline stub                                               | response 400, "Screenshots are not available on this server."                                                                                                                                                                                                                                                                                                                                                                                                 | Chấp nhận upload thật                                                                                                                       | ⚠️ Có nhưng có giới hạn |
| Favourites (đọc) stub      | `GET /web/osu-getfavourites.php` — inline stub                                             | response rỗng                                                                                                                                                                                                                                                                                                                                                                                                                                                 | Trả favourites thật                                                                                                                         | ⚠️ Có nhưng có giới hạn |
| Favourites (thêm) stub     | `GET /web/osu-addfavourite.php` — inline stub                                              | response rỗng                                                                                                                                                                                                                                                                                                                                                                                                                                                 | Thêm thật                                                                                                                                   | ⚠️ Có nhưng có giới hạn |
| Rate map stub              | `GET /web/osu-rate.php` — inline stub                                                      | trả về `"not ranked"` — một mã response thật của bancho.py, tái dùng chứ không bịa ra                                                                                                                                                                                                                                                                                                                                                                         | Chấp nhận rating thật khi map có leaderboard                                                                                                | ⚠️ Có nhưng có giới hạn |
| Comment stub               | `POST /web/osu-comment.php` — inline stub                                                  | response rỗng                                                                                                                                                                                                                                                                                                                                                                                                                                                 | Lưu comment thật                                                                                                                            | ⚠️ Có nhưng có giới hạn |
| Đăng ký tài khoản          | `POST /users` — inline stub                                                                | đăng ký tài khoản trong game — trả về đúng dạng lỗi "registration disabled, use the website" thật của bancho.py                                                                                                                                                                                                                                                                                                                                               | Có thể cho phép đăng ký thật tuỳ config                                                                                                     | ⚠️ Có nhưng có giới hạn |
| Star rating                | `POST /difficulty-rating` — inline                                                         | tính star rating cục bộ qua `IBeatmapDifficultyCalculator` cho beatmap id trong `?b=` (có thể mod qua `?mods=`, một bitmask), và cache kết quả no-mod vào `Beatmaps.Diff` để không phải tính lại mỗi lần gọi. Bản gốc chỉ redirect tới một trang web osu.ppy.sh mở trong browser hệ thống — server này không có trang như vậy, nên nó trả JSON (`{beatmap_id, mods, rating}`) trực tiếp thay vào đó                                                           | Redirect tới trang web osu.ppy.sh                                                                                                           | ⚠️ Có nhưng có giới hạn |

#### Ví dụ request/response chi tiết

**Leaderboard — `GET /web/osu-osz2-getscores.php`**

Query bắt buộc: `us` (username), `ha` (password MD5), `s` (`0`/`1`, "gọi từ song-select trong editor"), `v`
(kiểu leaderboard — số, xem `LeaderboardType`), `c` (md5 map), `f` (tên file map), `m` (mode, số), `mods` (bitmask).

```
GET /web/osu-osz2-getscores.php?us=PlayerOne&ha=<md5 pass>&s=0&v=1&c=9f8c2e1a...&f=xi+-+Frontier.osu&m=0&mods=0
```

Response (text thuần, không JSON), theo từng trường hợp:

| Trường hợp                                         | Body                                                |
|----------------------------------------------------|-----------------------------------------------------|
| Auth sai                                           | `401`                                               |
| Map chưa nộp (không tồn tại)                       | `-1\|false`                                         |
| Map cần cập nhật (checksum lệch)                   | `1\|false`                                          |
| Map không có leaderboard (ranked status không đạt) | `{status}\|false` (`{status}` là mã `RankedStatus`) |
| Có leaderboard                                     | nhiều dòng — xem bên dưới                           |

Body "có leaderboard" (mỗi dòng phân cách `\n`):

```
1|false|111222|5000|3|0|
0
xi - Frontier
5.42
<dòng personal-best, hoặc rỗng>
9001|PlayerOne|987654|512|0|10|480|1|8|100|1|0|7|1|1700000000|1
9002|PlayerTwo|900000|400|2|20|400|3|4|90|0|0|8|2|1700000100|1
```

Mỗi dòng score: `id|tên|điểm|maxCombo|n50|n100|n300|nMiss|nKatu|nGeki|perfect(0/1)|mods|userId|hạng|time|1`.

**Tìm beatmap — `GET /web/osu-search.php`**

Query: `u`, `h` (auth), `r` (ranked status, số), `q` (từ khoá), `m` (mode), `p` (số trang).

```
GET /web/osu-search.php?u=PlayerOne&h=<md5 pass>&r=0&q=frontier&m=0&p=0
```

Response (text, dòng đầu = số kết quả — `101` nghĩa là "trang đầy, còn nữa"; mỗi dòng sau là 1 beatmap set):

```
1
5000.osz|xi|Frontier|MapperName|1|10.0|2024-01-01 00:00:00|5000|0|0|0|0|0|[5.42⭐] Insane {cs: 4 / od: 8 / ar: 9 / hp: 6}@0
```

**Nộp điểm — `POST /web/osu-submit-modular-selector.php`**

Multipart form fields: `score` (dữ liệu điểm mã hoá, dạng base64 text — cùng field name còn có thể mang cả file
`.osr` nếu client gửi kèm replay), `s`, `iv`, `osuver`, `pass`, `c1`, `sbk`, `bmk`, `st`, `ft`. Đây là request nội
bộ do chính client osu! tạo ra khi hoàn thành 1 bài — không phải thứ bạn tự soạn tay được (payload đã mã hoá bằng
key riêng của từng phiên bản client).

Response (text thuần):

| Trường hợp                                    | Body                                                                                                                                 |
|-----------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------|
| Đậu bài, submit thành công                    | block "chart" pipe-delimited: `beatmapId:`, `chartId:beatmap`, `rankBefore:`/`rankAfter:`, `chartId:overall`, `achievements-new:`... |
| Rớt bài (fail)                                | `error: no`                                                                                                                          |
| Beatmap không tìm thấy                        | `error: beatmap`                                                                                                                     |
| Player không tìm thấy (hiếm, client tự retry) | rỗng                                                                                                                                 |
| Điểm trùng lặp                                | `error: no`                                                                                                                          |

**Info beatmap song-select — `POST /web/osu-getbeatmapinfo.php`**

Query: `u`, `h`. Body request **là JSON thật** (ngoại lệ trong nhóm text-format này):

```json
{ "Filenames": ["xi - Frontier (MapperName) [Insane].osu"], "Ids": [] }
```

Response (text, 1 dòng mỗi file khớp): `{index}|{id}|{setId}|{md5}|{status}|{grade0}|{grade1}|{grade2}|{grade3}`
(4 grade tương ứng 4 mode gốc osu!/Taiko/CTB/Mania, `"N"` nếu chưa có điểm nào ở mode đó).

```
0|111222|5000|9f8c2e1a...|2|S|N|N|N
```

**Star rating — `POST /difficulty-rating`** (endpoint JSON thật duy nhất trong nhóm này)

Query: `b` (beatmap id, bắt buộc), `mods` (bitmask, mặc định `0`).

```
POST /difficulty-rating?b=111222&mods=64
```

Response:

```json
{ "beatmap_id": 111222, "mods": 64, "rating": 5.87 }
```

Lưu ý `beatmap_id` giữ nguyên snake_case (không bị đổi thành `beatmapId`) — đây là object trả trực tiếp với tên
field viết sẵn snake_case trong code, không đi qua policy camelCase như các endpoint ở [
`api-external.md`](api-external.md).
`404` (text) nếu beatmap không tồn tại trong DB; text lỗi khác nếu DB có hàng nhưng file `.osu` không có trên đĩa.

### 2.3 `b.` — redirect thumbnail/preview cũ

| API                | Cách dùng                                         | Mô tả                                                                                                                                                                                                                                                | osu! Bancho bình thường | So sánh |
|--------------------|---------------------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-------------------------|---------|
| Redirect asset CDN | `GET /{**path}` — `MapBeatmapAssetGroup` (inline) | catch-all: 301-redirect mọi request thẳng tới `https://b.ppy.sh{path}` (thumbnail, preview) — không đổi bởi offline pivot; host này liên quan tới asset CDN của osu.ppy.sh, không liên quan tới storage beatmap/avatar/seasonal riêng của server này | Có, hành vi giống hệt   | ✅ Có    |

Ví dụ: `GET https://b.basil.example/thumb/5000l.jpg` → `301 Location: https://b.ppy.sh/thumb/5000l.jpg`.

### 2.4 `a.` — avatar (offline pivot)

bancho.py không có storage avatar cục bộ nào cả (host `a.<domain>` của nó luôn forward tới một CDN thật). Server
này lưu avatar phẳng dạng `{userId}.{ext}` dưới `Storage__AvatarsPath`.

| API                   | Cách dùng                                       | Mô tả                                                                                                                                                    | osu! Bancho bình thường                   | So sánh |
|-----------------------|-------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------|-------------------------------------------|---------|
| Phục vụ avatar cục bộ | `GET /{userId:int}` — `MapAvatarGroup` (inline) | phục vụ file đầu tiên khớp `{userId}.*` trong `Storage__AvatarsPath` (content-type png/jpg/jpeg/gif được suy ra từ extension); 404 nếu không có file nào | Luôn forward tới một CDN thật thay vào đó | ❌ Không |

Ví dụ: `GET https://a.basil.example/7` → `200`, `Content-Type: image/png`, body là bytes ảnh avatar user id `7`
(server tìm `7.png`, `7.jpg`... theo thứ tự); `404` nếu user chưa có avatar nào.
