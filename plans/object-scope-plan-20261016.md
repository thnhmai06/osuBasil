> **Status (2026-10-20).** Completed phases: Domain redesign, storage and migrations, the Room pilot, and the wider
> object-scope migration. The Runtime/BasilBot phase is partially complete: command and announcement handlers are
> implemented, and BasilBot is online with a connection of its own (`BotConnection`); a blocked user's channel messages are
> filtered per recipient by `ChannelSession.RecipientsAsync`. Remaining phases and work: Host migration (including `Basil.Cli`),
> test migration. Host checklist: transports must deliver channel messages only to `RecipientsAsync` and must skip the bot's
> connection when delivering; they show the bot as online. Hosts and tests still use old namespaces.

# Kế hoạch: chốt use case osuBasil develop, tổ chức lại Application quanh đối tượng sở hữu hành vi

Plan này (`plans/object-scope-plan-20261016.md`, đã duyệt 2026-10-16) là nguồn sự thật cho các layer sau; khi mâu thuẫn với
plan cũ, plan này thắng. Mục "Cách làm việc khi triển khai" chỉ dành cho phiên điều phối, không phải luật của code.

## Context

Báo cáo "Báo cáo đối chiếu use case osuBasil develop" (Claude Docs) so sánh develop (`2a7d9a6c`) với main và
bancho.py master. Qua nhiều lượt phỏng vấn, bạn đã chốt nghiệp vụ và một quy tắc thiết kế cốt lõi:

> **Hành vi được truy cập từ đối tượng sở hữu và quản lý nó**, qua một góc nhìn đã scope theo caller
> (`room.ScopeTo(connection)`), không qua một đối tượng bên ngoài như service hay Actor.

Kế hoạch gồm ba phần: (1) phân loại mới cho từng mục của báo cáo; (2) cách tổ chức lại Application theo quy
tắc trên, kèm danh sách chỗ vi phạm hiện tại; (3) các giai đoạn triển khai. Duyệt kế hoạch này nghĩa là xác
nhận kiến trúc ở mục 2. Phương án "Actor" đề xuất trước đó đã bị loại vì nó là God Actor.

Bằng chứng: 3 agent đọc mã nguồn develop, main và bancho.py (raw source); trang Silences của osu! wiki;
tài liệu `maxmind/geoip2-dotnet` qua Context7.

## 1. Phân loại (bản cập nhật cho báo cáo)

**Cần triển khai** (phần Domain, Application, Infrastructure, Bot; phần host thì ghi vào checklist host)

| Mục | Quyết định |
|---|---|
| Tài khoản đầu tiên | Server không tự tạo account. Quản trị viên đầu tiên (mọi permission) tạo bằng lệnh trong console của Host (project `Basil.Cli`, thư viện `XenoAtom.*`, cờ `--noconsole` tắt console). Console chạy lệnh với BasilBot (theo id) làm tác nhân và bỏ qua kiểm quyền. Việc này làm ở giai đoạn Host; bootstrap `admin`/`admin` đã bị bỏ. |
| Tên user unique giữa account còn sống | Xoá là soft delete (`DeletedAt`) và **vĩnh viễn**: không có thao tác phục hồi. Id không bao giờ dùng lại. Bỏ cột `safe_name`, thay bằng unique index một phần trên `lower(replace(name, ' ', '_'))` với điều kiện `deleted_at is null`; index trigram cũng chuyển sang biểu thức này. Domain có `NormalizedName` get-only, chuẩn hoá theo đúng luật đó. Tên của account đã xoá được dùng lại. |
| Trải phẳng `Permissions` | Mỗi member là một hành vi, tên dạng `[Resource][Action]`, chia `#region` theo resource. Trong enum không có member nào là group. Quyền lớn vẫn chứa bit của quyền nhỏ, vì đó vẫn là *một quyền*. Static class `PermissionsGroups` chỉ giữ mask **theo lĩnh vực** (`Chat`, `Multiplayer`, `Spectating`, `Beatmapsets`, `Users`, `Server`…), mỗi lĩnh vực kèm permission quản lý của nó. Thêm `ScoreSubmit` (chơi = nộp điểm, một bit). Bảng tên nháp ở mục 2. |
| `Privileges` (đổi tên từ `BanchoPrivileges`) | Permission diễn tả quyền; privilege diễn tả vai trò ở tầng cao hơn. Một enum flags duy nhất, giữ thứ tự và giá trị bit của osu!/bancho.py: `Player, Moderator, Supporter, Owner, Developer, Tournament`. Extension `privilege.Permissions` liệt kê các permission trực tiếp của vai trò đó (thay các group theo vai trò). **User có bit X khi giữ ít nhất 1 permission trực tiếp (depth 1) của X**, tính trên effective permission. Một yêu cầu viết bằng privileges được thoả khi user có đủ mọi bit được yêu cầu. Privilege chỉ để hiển thị và để so khớp yêu cầu, không cấp quyền: client osu! có thể bật UI theo vai trò, nhưng nếu thiếu permission thật thì server vẫn từ chối. |
| Quyền kênh general | `GeneralChannel` đổi `ReadPermissions`/`WritePermissions` thành `ReadPrivileges`/`WritePrivileges`; user đọc hoặc ghi được khi có đủ các bit yêu cầu. |
| Quyền trong room | Enum flags riêng `RoomPermissions` (ví dụ `SettingsChange`, `SlotsLock`, `MembersKick`, `MembersBan`, `RoundsStart`, `RoundsAbort`, `RefereesManage`, `ChatPost`…). Vai trò cố định trong Domain (creator, referee, host, player, người ngoài), mỗi vai trò có một tập `RoomPermissions` cố định. **Vai trò khớp mô hình của osu!**: hành vi gốc của player và host luôn được phép theo vai trò, `RoomPermissions` không chia nhỏ hơn, vì client osu! tự thực hiện chúng trên UI. Quyền chi tiết chỉ dùng cho đường ngoài client (lệnh chat, API). `ScopeTo` tính quyền room = quyền theo vai trò ∪ quyền ánh xạ từ permission chung (ví dụ `RoomManageAny` → quyền quản lý; `ChatPost` → chat room), rồi trừ phần bị restriction treo (người quản lý lĩnh vực không bị treo). Room không cấp vượt qua restriction. Chỉ room có quyền cục bộ trong đợt này. |
| Group của các flags enum khác | Cùng mẫu `{Enum}Groups`: `GameModsGroups` (`SpeedChanging`, `Key`, `OsuSpecific`, `ManiaSpecific`) và `BanchoFlagsGroups.CheatSigns`. Gỡ các member tổ hợp khỏi enum; bit trên wire không đổi. |
| Restriction | Treo một mask `Permissions` và chỉ chứa dữ liệu giới hạn, không có note hay lý do. Các mask có tên định nghĩa trên Restriction, theo đúng osu! wiki: `Silence = Chat \| Multiplayer` (không chat ở bất kỳ đâu: general, PM, chat room, chat `#spec`; không tham gia multiplayer; vẫn được xem người khác chơi). `Restrict = Silence \| Spectating \| ScoreSubmit \| BeatmapsetUpload \| ScreenshotUpload`. Chỉ có một thao tác restrict, nhận mask; mask được chứa bất kỳ bit nào. **Người có quyền quản lý một lĩnh vực thì không bị restrict ở các quyền thuộc lĩnh vực đó**: restriction vẫn được tạo, nhưng `Effective` giữ nguyên mọi bit của lĩnh vực mà người đó quản lý. Ví dụ: người có `RoomManageAny` bị silence vẫn chơi và quản lý multiplayer, nhưng không chat được nếu không quản lý Chat. Mỗi lĩnh vực là một group trong `PermissionsGroups`, kèm permission quản lý của nó (bảng ánh xạ là tri thức Domain). Luật này thay cho luật cũ "mask không được chứa management". |
| Hệ quả theo group | **Chat** (`ChatPost` cho mọi loại kênh: general, room, `#spec`; `ChatPostPrivate` cho PM) bị cấm. **Multiplayer**: bị đẩy khỏi mọi room, bị xoá khỏi danh sách referee của các room đang mở, vai trò creator tạm vô hiệu, không join hay chat được room nào. **Spectating**: không xem được ai, đang xem thì bị dừng; người khác vẫn xem họ được. **ScoreSubmit**: không nộp điểm. **Upload**: không upload beatmap hay screenshot. |
| Gói silence của osu! | `SilenceEnd` và `UserSilenced` chỉ gửi khi mask chứa đủ `Restriction.Silence`, vào lúc restrict và lúc login (khớp osu! wiki). Việc dịch sang packet thuộc host. |
| Moderation log | Append-only, giống bancho.py: action, by, target, reason, thời điểm. Ghi các việc restrict, lift, note, và mỗi lần ai đó xem lịch sử login. Chỉ để truy vết, không ảnh hưởng quyền. |
| Block | Một chiều; mỗi cặp chỉ có một bản ghi, là friend hoặc block. Block có bốn tác dụng: chặn PM (`user_dm_blocked`); lọc im lặng tin trong kênh chung theo người nhận; chặn spectate người đã block mình; chặn invite gửi tới người đó. |
| Luật "chặn, trừ quyền cao hơn" | Lệnh chặn do user đặt (block, PM chỉ cho friend…) không giới hạn người có quyền quản lý khu vực tương ứng. Nháp: PM và kênh chung bỏ qua bằng `ChatMessageAnyone`, invite bằng `RoomManageAny`, spectate bằng `RoomObserve`. |
| Friends | Thêm và xoá friend, không cần target online; gửi danh sách friend lúc login; `osu-getfriends.php`. |
| Presence visibility | Lưu trên `BanchoConnection`, dùng enum `PresenceVisibility` có sẵn. Chỉ lọc tin server tự đẩy; khi client hỏi thì vẫn trả lời; logout vẫn gửi. |
| `osu-getbeatmapinfo.php` | Nhận `{"Filenames","Ids"}`, bỏ qua `Ids`. Tên file tính như bancho.py: `"{Artist} - {Title} ({Creator}) [{Version}].osu"`, bỏ các ký tự `:\/*<>?"\|`. Tra bằng một criterion mới của `BeatmapQuery`. |
| Country lúc đăng ký qua game client | Không dùng dịch vụ bên thứ ba. Thứ tự: header proxy (`CF-IPCountry`, `X-Country-Code`), rồi file GeoLite2 offline qua `MaxMind.GeoIP2` (`DatabaseReader`, `FileAccessMode.MemoryMapped`), cuối cùng `Xx`. Toàn bộ việc đọc header và tra GeoLite thuộc host (một middleware nhỏ, vì thư viện chỉ có DI cho web service online). Domain và Application chỉ nhận `Country` như một input của đăng ký, không biết country được xác định bằng cách nào. Login không sửa country. |
| Spectator khi host đóng | Trong cùng thao tác đóng connection, dưới lock của host: gỡ hết spectator và phát `UserSpectatorLeft` cho từng người. Sửa luôn race giữa `Spectate` và lúc host đóng. |
| Chat room (#8) | Ai join được room (`RoomJoin` còn hiệu lực, không bị ban, đúng password) thì đọc và chat được trong `#mp_x`, kể cả khi không ngồi slot. |
| Leaderboard | Giống bancho.py: Global, Mods, Friends, Country; top 50 điểm tốt nhất theo score, kèm điểm của chính mình. Phản hồi submit luôn báo rank #1 để client gửi replay. `UserStats` giữ cố định `Pp = 727`, `Accuracy = 100`, `Rank = UserId`, không lưu DB. |
| Lịch sử login/IP | Permission mới `UserViewLogins`, nằm trong `UserRestrict`. Mỗi lần xem được ghi vào moderation log. |
| Quyền của host | Ít nhất là mọi việc client osu! cho host làm: map, mods, freemod, win condition, team mode, tên, password, khoá slot, kick, chuyển host, start; thêm start force và abort. Host không được ban, quản lý referee, seat người khác, di chuyển hay đổi team người khác, đổi private, đóng room. |
| BasilBot vào server | Đảo luật "No bot in the server". Bot là user id 1, tạo bằng migration, luôn online, là friend ngầm của mọi người, không login được, không có permission. Bot là giọng của server trong chat: trả lời lệnh và gửi thông báo hệ thống. Record của bot cố định (không xoá, không đổi permission hay password, không restrict, không revoke); metadata (tên, country, avatar) vẫn sửa được. |
| Lệnh chat | Gồm toàn bộ `!mp` (thêm `randpw`), các lệnh chung, và lệnh staff (restrict, lift, note, xem log, `!block`/`!unblock`). Prefix cố định `!`. Lệnh là một entry point: chạy trên đối tượng đã scope theo connection của người gõ; bot chỉ trả lời. |
| Countdown của room | Start có hẹn giờ (ai được start thì được hẹn), timer trơn, huỷ timer; abort round cũng huỷ hẹn start. Có API đọc trạng thái timer; SSE chờ Hosts. |
| Bỏ delegation | Bỏ `TournamentActForUsers`, `RoomDelegation`, `ActFor`, `DelegatedConnection`, `DelegationFailure`. |
| Kiểm quyền tập trung | Theo mục 2: `ScopeTo` cùng authority dùng chung. Bao gồm 5 permission hiện chưa có chỗ nào kiểm. |

**Đã có, không làm lại:** tra beatmap (`BeatmapQuery`); quản lý room qua API; từ chối điểm sai integrity.

**Chủ động không triển khai** (ghi vào `working-scopes.md`): multi-account, hardware ban, client hash,
`Verified`; chặn client cũ; cập nhật country từ IP cho account đã có; dùng API để ngồi slot hoặc làm host
thay người chơi; PM cho người offline.

**Phụ thuộc phần khác:** SSE và overlay; route API của room và timer; mọi bản dịch event sang packet, IRC
hoặc web route; các lỗi bảo mật đã ghi trong host (set ẩn, `/d/{id}`, path traversal ở seasonal). Tất cả chờ
Hosts.

## 2. Thiết kế: hành vi trên đối tượng sở hữu, truy cập qua `ScopeTo`

### Rà soát: code hiện tại vi phạm ở đâu
| Chỗ | Anti-pattern | Đối tượng thực sự sở hữu |
|---|---|---|
| `IRoomService` cùng `Members`/`Authority`/`Settings`/`Slots`/`Rounds`, mọi method nhận `Room` | Thiết kế thủ tục quanh service | `Room`: đã có sẵn các phần `RoomSlots`/`RoomSlot`, `RoomRounds`, `RoomSettings`, `RoomAuthority`, `RoomMembers`, nhưng chúng chỉ là data |
| `IChannelService` (`Members`, `Messages`, `General`) nhận `ChannelSession` | Thiết kế thủ tục quanh service | Kênh: thành viên, tin nhắn, cấu hình |
| `ISessionService.Connections.Bancho.Spectators` nhận host | Thiết kế thủ tục quanh service | `BanchoConnection` của host (đang giữ `Spectators`) |
| `ISessionPresenceService` (`SetAway`, `SetPmPrivate`) | Thiết kế thủ tục quanh service | `UserSession` |
| `IUserService` / `IUserRestrictionsService` nhận `User` | Thiết kế thủ tục quanh service | User: restriction, permission, xoá, quan hệ |
| `ILobbyService` (`Rooms`, `Watchers`) | Thiết kế thủ tục quanh service | `Lobby` |
| `IBeatmapsetService.DeleteAsync`, khoá/ẩn qua repository | Thiết kế thủ tục, cộng ghi thẳng không qua kiểm quyền | `Beatmapset` |
| `PermissionRules` / `RoomRules` / `ChannelRules` được gọi rải rác trong service | Truyền quyền lòng vòng qua nhiều tầng | Mỗi đối tượng tự kiểm quyền khi `ScopeTo` |
| Setter `internal` trên runtime model, service sửa trực tiếp | Lộ state có thể sửa | Đối tượng tự sửa state của nó trong hành vi |

### Quy tắc tổ chức
- **Domain** (`XRecord` + `XData`): giá trị hợp lệ, lưu bền, không hành vi. Ví dụ: `UserRecord`/`UserData`,
  `BeatmapsetRecord`/`BeatmapsetData`, `RestrictionRecord`/`RestrictionData`, `ScoreRecord`, `MatchRecord`…
  Kèm tri thức trên chính giá trị: `Permissions`, `PermissionsGroups`, `BanchoPrivileges.Permissions`,
  `NormalizedName`, tên file osu! của beatmap.
- **Application object** (`X`, nằm trong `Services.Contracts`): đại diện một khái niệm nghiệp vụ, sở hữu state
  và invariant, và cung cấp hành vi. Ví dụ: `User`, `UserSession`, `Connection` (các lớp con), `Room` (bản ghi
  là `MatchRecord`), `RoomSlot`, `Lobby`, các loại kênh, `Beatmapset`, `Restriction`, `Score`, cùng đối tượng gốc
  `Server` (settings, content, kênh general, announce). State chỉ sửa được bên trong hành vi của chính nó.
  Collaborator (event stream, `TimeProvider`, repository) được inject lúc tạo; implementation nội bộ nằm ở
  `Services.Implementations`.
- **Khi nào dùng scope:** mọi hệ thống hành động dựa trên permission (hoặc privileges) đều theo mẫu scoped. Chỉ
  không scope khi hệ thống đó không cần kiểm quyền: việc nội bộ của server, handler Runtime, startup, bot gửi
  thông báo, đọc dữ liệu công khai.
- **`X.ScopeTo(connection)`** trả `ScopedX`: một capability ngắn hạn, dùng cho một request hoặc một lệnh.
  Lúc tạo scope, nó **chụp** effective permission và vai trò của caller trên X (creator, referee, host, player,
  owner…). Mỗi hành vi kiểm trên bản chụp đó và trả `NotAuthorized` hoặc `Restricted` ngay nếu không được phép;
  được phép thì vào lock của X, giữ invariant, thực thi, phát event. Phần con cũng đi qua scope:
  `scopedRoom.Slots[3].LockAsync()`. Caller không truyền `connection`, permission hay actor vào từng lời gọi.
- **Không giả scoping**: hành vi và việc kiểm quyền nằm trong module của X (`Room` cùng `ScopedRoom`), không nằm
  trong service trung tâm. Các primitive kiểm quyền dùng chung (`Access`, effective permission, hierarchy, luật
  "chặn, trừ quyền cao hơn") là thư viện mà mỗi `ScopeTo` gọi, không phải tầng để đi qua.
- **Service còn lại** cho truy vấn, persistence, hạ tầng và điều phối nhiều aggregate: đăng nhập và đăng ký
  (`IAuthService`), import và scan beatmapset, nộp điểm (ghi vào round của room và stats của user), chấm
  anticheat, báo cáo match, leaderboard. Không service nào nhận object rồi làm thay hành vi của object đó.
- **Registry** (users online, lobby, kênh general) chuyển sang Services, vì giờ chúng giữ object có hành vi.
  Storage chỉ còn repository, file storage và query record.
- **Đối tượng gốc**: host tìm đối tượng qua registry hoặc repository (`rooms.FindAsync(id)`,
  `users.FindAsync(id)`, `server`), rồi `ScopeTo(connection)`.
- **`User` và `UserSession` tách riêng.** `User` là tài khoản, dù online hay offline: restriction, quan hệ,
  tên, permission, xoá. `user.Session` là một lần online (null khi offline), so sánh theo reference: away,
  presence, PM, connection.

```csharp
var room = await lobby.FindAsync(roomId);
var scoped = room.ScopeTo(connection);           // chụp quyền và vai trò
await scoped.Slots[3].LockAsync();
await scoped.StartAsync(countdown: TimeSpan.FromSeconds(10));

var target = await users.FindAsync(userId);
await target.ScopeTo(connection).Restrictions.ImposeAsync(Restriction.Silence, endsAt, reason);
```

### Bảng tên permission (nháp; `⊃` nghĩa là chứa bit của)
| Resource | Member |
|---|---|
| Room | `RoomCreate`, `RoomJoin`, `RoomCreateUnlimited`, `RoomViewPrivate`, `RoomObserve` ⊃ `SpectatingWatch`, `RoomManageAny` ⊃ `RoomViewPrivate`, `ChatPostAnyRoom` |
| Chat | `ChatPost`, `ChatPostPrivate`, `ChatPostAnyRoom`, `ChatMessageAnyone`, `ChatAnnounce` |
| Spectating / Score | `SpectatingWatch`; `ScoreSubmit` |
| Beatmapset | `BeatmapsetUpload`, `BeatmapsetDirect`, `BeatmapsetViewHidden`, `BeatmapsetManage` ⊃ `BeatmapsetViewHidden` |
| Screenshot | `ScreenshotUpload`, `ScreenshotDelete` |
| User | `UserSilence`, `UserRestrict` ⊃ `UserSilence`, `UserViewLogins`, `UserNote`, `UserViewDeleted`, `UserManage` ⊃ `UserViewDeleted`, `UserManagePermissions` |
| Server | `ServerConfigure`, `ServerViewDiagnostics`, `ServerManageContent` |

### Còn để chốt lúc triển khai
Tôi sẽ đề xuất và hỏi lại ở giai đoạn tương ứng:
- Tên các object kênh: giữ `XChannelSession` hay đổi theo mẫu mới.
- Match đã đóng có object Application không, hay chỉ đọc bằng query.
- Danh sách đầy đủ vai trò mà `ScopeTo` chụp cho từng object.

### AGENTS.md (và skill code-principles) cần sửa
- Thay "Storage holds data, services hold behaviour", "object stays a parameter, never an object-bound handle"
  và "a class named like a model holds no logic" bằng quy tắc đối tượng sở hữu hành vi cùng `ScopeTo`; ghi rõ
  5 anti-pattern.
- "RoomService checks authority" đổi thành: kiểm quyền khi `ScopeTo`, dùng primitive authority chung.
- Thêm quy ước tên `XRecord`/`XData` ở Domain và `X` ở Application; mẫu `{Enum}Groups`; `PermissionsGroups`
  theo lĩnh vực; `Restriction.Silence`.
- Thay mục "`ClientPrivileges` is never stored… derived (`ToPrivileges()`)" bằng `Privileges` (bit bật khi có ≥1
  permission trực tiếp; privilege không cấp quyền). Thêm `RoomPermissions` và luật "vai trò khớp mô hình osu!".
- Đảo "No bot in the server", "chat commands belong to clients" và "a countdown is a client feature"; bỏ
  delegation. Account đầu tiên do console tạo (không bootstrap tự động).
- Restriction: thay "a mask containing a management permission is invalid" bằng "người quản lý một lĩnh vực không
  bị restrict ở lĩnh vực đó".
- Thêm: không dùng dịch vụ bên thứ ba cho việc lõi, trừ mirror; luật "chặn, trừ quyền cao hơn"; tên chỉ unique
  giữa các account còn sống; xoá là vĩnh viễn.

## 3. Giai đoạn triển khai

**Cách làm việc khi triển khai:**
- Phiên điều phối chạy **Sonnet High**; bạn tự chuyển bằng `/model` và `/effort`, vì tôi không tự đổi model được.
  Gặp vấn đề khó, hội tụ chậm, hoặc trước khi chốt một giai đoạn, thì hỏi `advisor`.
- Tôi chỉ đạo và giám sát agent con, cả về hành vi lẫn chất lượng sản phẩm.
- Agent con gồm model OpenCode Go qua `http://127.0.0.1:4096`, và subagent Sonnet/Haiku với effort vừa đủ.
- **OpenCode Go đã hết quota**, nên chỉ dùng model free: `opencode/*-free` và free tier của Go. Cụ thể:
  - Trước mỗi đợt chạy `opencode models` và `opencode stats` để biết model free nào còn dùng được.
  - Việc cơ học giao cho model free.
  - Việc cần đúng thiết kế giao cho Sonnet; việc nhỏ hoặc tra cứu giao cho Haiku.
- **Tận dụng triệt để công cụ:** có công cụ phù hợp thì bắt buộc dùng, cho cả tôi lẫn mọi prompt giao agent con.
  - **Rider MCP** trước tiên: `rename_refactoring` (đổi tên `X` thành `XRecord`, đổi tên permission,
    `SafeName` thành `NormalizedName`), `move_type_to_namespace` (chuyển runtime model sang Services.Contracts),
    `safe_delete` (bỏ delegation, bỏ `IRoomService`…), `change_api_signature`, `extract_interface`,
    `get_file_problems`, `lint_files`, `build_solution_*`, `search_symbol`, `find usages` qua `analyze_calls`,
    `execute_sql_query` / `introspect_schema` để kiểm migration.
  - **CodeGraph** (`codegraph_explore`) trước khi grep hay đọc file.
  - **Context7 / Microsoft Learn**: tra API .NET, `MaxMind.GeoIP2`, Npgsql/Dapper, extension member của C# 14.
  - **postgres MCP**: thử index biểu thức và migration trên DB dev.
  - **Skills**: `code-principles` (mọi bước), `caveman:safe-refactor` hoặc `dotnet:csharp-refactoring` cho đợt
    đổi tên lớn, `migrate-nullable-references` nếu cần, `postgresql-best-practices` cho index và migration,
    `dotnet-claude-kit:build-fix` khi build lỗi, `code-review` / `simplify` để review cuối mỗi giai đoạn,
    `superpowers:verification-before-completion` trước khi báo xong.
  - **Plugin dotnet-msbuild (binlog)** khi chẩn đoán lỗi build.
- Rider offline thì ghi chú lại, rồi chuyển sang CodeGraph và CLI.
- Tôi review từng dòng trước khi commit và push.

0. **Ghi chỉ dẫn vào `~/.claude/CLAUDE.md`** (dùng chung mọi project), cập nhật mục "Delegating implementation to
   OpenCode":
   - Vai trò chỉ đạo và giám sát (hành vi agent con và chất lượng sản phẩm).
   - Model triển khai mặc định là Sonnet High, có vấn đề thì hỏi advisor.
   - Agent con là OpenCode Go (`0.0.0.0:4096`) cùng subagent Sonnet/Haiku với effort hợp lý.
   - Khi quota Go hết thì chỉ dùng model free.
   - Tận dụng triệt để MCP, skills, plugins (Rider trước tiên) cho chính mình và trong mọi prompt giao việc.
1. **Tài liệu quyết định**: AGENTS.md, `working-scopes.md`, ghi chú vào các plan cũ đã bị thay thế, cập nhật
   doc báo cáo, memory.
2. **Domain**: đổi tên `X` → `XRecord` (Rider); `NormalizedName`; trải phẳng `Permissions` + `PermissionsGroups`
   (lĩnh vực); đổi `BanchoPrivileges` thành `Privileges` kèm `privilege.Permissions`; `RoomPermissions` + bảng
   vai trò; quyền kênh general bằng privileges; `GameModsGroups`, `BanchoFlagsGroups`; `Restriction.Silence`/`Restrict`;
   `ModerationLogEntry`; tên file osu! của beatmap; bỏ delegation.
3. **Storage và migration**: bỏ `safe_name`, thêm unique index một phần; ánh xạ lại bit permission đã lưu;
   bảng moderation log; đọc lịch sử login; criterion filename; listing leaderboard; user id 1 cho bot.
4. **Pilot `Room`**:
   - Chuyển `Room` và các phần của nó sang Services.Contracts dưới dạng object có hành vi, kèm `ScopedRoom`.
   - Gộp hành vi từ `RoomService` và các child service vào `Room`; bỏ `IRoomService`.
   - Countdown, luật host mới, chat room cho người join được room.
   - **Dừng lại để bạn review mẫu trước khi nhân rộng.**
5. **Nhân rộng** cùng mẫu: `Lobby`, các kênh, `UserSession`/`Connection` (spectator, presence, dọn spectator
   khi host đóng), `User` (restriction và hệ quả, quan hệ friend/block, permission, xoá, moderation log, lịch
   sử login), `Beatmapset`, `Score`, `Server` (settings, content, kênh general, announce). Service chỉ còn đúng
   các vai trò nêu ở mục 2.
6. **Runtime và bot**: bot connection; bề mặt lệnh chat chạy trên object đã scope; handler
   thông báo hệ thống; bỏ `BotOptions.Prefix` và 6 port `IBasil*`.
7. **Checklist host** (ghi vào báo cáo, chưa làm): packet silence, friends, presence; route `getfriends`,
   `getbeatmapinfo`, `getscores`; middleware GeoIP; API cho room, timer, moderation, friends.

## Verification

- Build các project đã migrate:
  `dotnet build src/Domain`, `src/Application/Storage/Implementations`, `src/Application/Services/Implementations`,
  `src/Infrastructure/Storage`, `src/Infrastructure/Services`, `src/Infrastructure/Runtime`, `src/Bot/Application`.
  Không build cả solution, vì host và test chưa migrate.
- Chạy Rider MCP `get_file_problems` / `lint_files` cho các file đã đổi.
- Grep phải sạch:
  - không còn service nào nhận object rồi làm thay hành vi của nó (`Room room,` trong tham số service);
  - không còn setter `internal` bị code ngoài đối tượng gọi;
  - không còn `PermissionRules` bị service gọi;
  - không còn `is XConnection` dùng để quyết định quyền.
- Review từng dòng theo checklist "Final verification" của AGENTS.md (đã cập nhật) và 5 anti-pattern.
- Hiện chưa có project test chạy được. Test cho các luật quyền sẽ viết khi migrate test project; đợt này không
  tạo project test mới, trừ khi bạn yêu cầu.
