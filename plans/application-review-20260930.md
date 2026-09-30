# Review toàn bộ `Basil.Application` — 2026-09-30

**Trạng thái:** đã xử lý theo `plans/application-environment-plan-20260930.md` (phase 0–10, 2026-10-01, chưa commit); phụ lục F của plan ghi phần còn lại. Review trên `develop` với working tree chưa commit
(kế hoạch identity-wrapper bước 1–5).

**Phạm vi:** mọi file `.cs` trong `src/Basil.Application` (92 file). **Không review** `docs/` và
XML doc theo yêu cầu: chúng được viết lại khi source ổn định.

**Thước đo:** các nguyên tắc trong `AGENTS.md` mục *Domain and Application*, nhất là hai nguyên
tắc chốt hôm nay:

1. **Phía bị tác động sở hữu tương tác.** Tác nhân chỉ gọi hành động. Object bị tác động lưu quan
   hệ và phát event.
2. **Application phát event, không tiêu thụ event.** Handler, dispatcher, định tuyến event →
   notification là việc của Infrastructure và Host.

Và định hướng: Application là **mô phỏng một môi trường** cho các layer khác quan sát và cắm vào,
không phải một dây chuyền "làm A rồi báo B rồi lưu C".

**Ký hiệu mức độ:** **[N]** nghiêm trọng (hỏng chức năng chính hoặc hỏng state), **[C]** cao,
**[T]** trung bình, **[Th]** thấp. "Chưa dùng trong Application" nghĩa là không có chỗ dùng trong
project này; Infrastructure và Host hiện không build nên chưa thể nói là dead code.

---

## 1. Mô hình đích: ai sở hữu gì, ai phát event gì

Bảng này là đề xuất. Các ô đánh dấu (QĐ) cần người dùng chốt ở mục 9.

| Object (bị tác động) | Sở hữu | Hành động (tác nhân gọi) | Event phát ra |
|---|---|---|---|
| Nơi user online (tên tạm `Presence`, thay `ISessionRegistry`) (QĐ) | tập session online (game + IRC), trạng thái hiển thị | `Connect`, `Disconnect`, cập nhật status | `SessionOpened`, `SessionClosed`, `StatusChanged` |
| `ChannelSession` (kênh chat) | thành viên | `Join(session)`, `Part(session)`, `Post(session, text)` | `MemberJoined`, `MemberParted`, `MessagePosted` |
| Hội thoại riêng (QĐ) | hai người tham gia | `Post(from, text)` | `MessagePosted` |
| Danh mục kênh (thay `IChannelRegistry`) | các kênh đang sống; luật auto-join | `Open`, `Close`, auto-join khi có session mới | `ChannelOpened`, `ChannelClosed` |
| `Lobby` (thay `IRoomRegistry`) | các room; khóa độc quyền `EnterAsync` | `Open(...)`, `Close(room)` | `RoomOpened`, `RoomClosed` |
| `Room` | slot, người chơi, host, referee, ban, invite, **kênh của room**, **countdown**, **vòng đời round** | `Join(session, password)`, `Leave`, `Kick`, `Ban`, `Invite(by, user)`, `Start`, `Abort`, báo loaded/skip/fail/complete, countdown | các `RoomEvent` hiện có, thêm round progress, countdown, `ScoreSubmitted` |
| Luồng spectate của một người chơi (QĐ) | danh sách người xem | `Join(spectator)`, `Leave(spectator)` | `SpectatorAdded`, `SpectatorRemoved` |
| `GameSession`/`IrcSession` | chỉ dữ liệu của chính user online: login, client, `AwayMessage` | là **tác nhân**, không lưu quan hệ, không phát event | — |

Hệ quả: câu hỏi "user X đang ở kênh/room nào, đang xem ai" là truy vấn trên object bị tác động
(hoặc chỉ mục của object môi trường), không phải state trên X.

---

## 2. [A] Vi phạm "phía bị tác động sở hữu tương tác"

**A1 [C] Session lưu kênh và tự `Join`/`Part` kênh.**
`Sessions/UserSession.cs:23` (`_channels`), `:61` (`Join`), `Part`. `ChannelSession.Add/Remove`
là `internal` để chỉ session gọi được (`Chat/ChannelSession.cs:53`).
Thêm một loại "joinable" mới là phải thêm `Join*` và một set mới vào session.
**Sửa:** xóa `UserSession.Channels/Join/Part`. `ChannelSession.Join(session)` kiểm tra quyền đọc,
thêm member, phát `MemberJoined`; `Part` tương tự. Câu hỏi "kênh của X" = lọc các kênh theo
member (danh mục kênh có thể giữ chỉ mục ngược nếu cần tốc độ). Cập nhật nơi gọi:
`Gateway.cs:80`, `Gateway` lúc disconnect, `Lobby.cs:70`, `RoomLifecycleCommands.cs:73`.

**A2 [C] Session là nguồn event.**
`UserSession : IEventPublisher<SessionEvent>` (`UserSession.cs:24`). `GameSession.Status` tự phát
`StatusChanged` (`GameSession.cs:49`). `Spectate` ghi event lên session của host.
**Sửa:** bỏ event channel khỏi `UserSession`. `StatusChanged` do object nơi user online phát
(QĐ-3). Event spectate do luồng spectate phát (A4).

**A3 [C] Session lưu slot/room và tự vào/ra room.**
`GameSession.Slot { get; internal set; }` (`GameSession.cs:68`), `Room => Slot?.Slots.Room`,
`JoinRoom`/`LeaveRoom` (`:112`, `:118`). `RoomSlot.Occupy/Clear/MoveTo` phải ghi ngược vào session.
**Sửa:** `Room.Join(session, password)` / `Room.Leave(session)`. "Room của X" do `Lobby` trả lời
(chỉ mục session → room trong Lobby, cập nhật dưới room scope). Bỏ `GameSession.Slot/Room`.
Đây là thay đổi lớn nhất vì `ScoreSubmission`, `MpCommands`, `Gateway` đều đọc `session.Room`.

**A4 [T] Spectate lưu ở cả hai phía session.**
`GameSession._spectators`, `Spectating`, `Spectate`, `StopSpectating` (`GameSession.cs:21`, `:80`).
**Sửa:** object "luồng spectate" của người được xem sở hữu danh sách người xem và phát event.
Người xem gọi `stream.Join(me)`. Vị trí sống của luồng này là QĐ-4.

**A5 [C] Luật vào room nằm ở command, không ở room.**
`RoomLifecycleCommands.JoinAsync` kiểm tra ban, private/invite, password, đang ở room khác, room
đầy (`RoomLifecycleCommands.cs:58–70`). `RoomSlots.Seat` chỉ kiểm tra ban. Luồng Bancho (packet
join match) gọi `session.JoinRoom(room)` sẽ bỏ qua private và password.
**Sửa:** `Room.Join(session, password)` tự áp mọi luật và trả kết quả có kiểu (không phải chuỗi
trả lời). Command và Bancho cùng gọi một chỗ.

**A6 [C] Thành viên kênh của room không đi theo người chơi của room.**
Chỉ `!mp join` cho session vào kênh room (`RoomLifecycleCommands.cs:73`). Rời room, bị kick, bị
ban, bị đẩy ra khi khóa slot đều **không** rời kênh. Chỉ `Lobby.CloseRoomAsync` mới part hết.
Hậu quả: người bị kick vẫn nhận chat room và vẫn gửi được vào đó (`Messaging` chỉ kiểm tra
membership). Xem thêm C7.
**Sửa:** `Room` tự giữ kênh của nó. Thành viên kênh = người đang ngồi + referee + creator +
observer tournament (`TOURNAMENT_JOIN_MATCH_CHANNEL` [bancho.py]). Vào room thì join kênh; rời
room thì part, trừ khi vẫn thuộc nhóm khác (referee, creator, observer). Xem C27 về quyền đọc.

**A7 [C] Luật "room trống thì đóng" nằm trong handler.**
`RoomEventHandlers.cs:74` gọi `lobby.CloseRoomAsync` khi mọi slot trống. Chuyển handler sang
Infrastructure mà quên luật này thì room không bao giờ tự đóng.
**Sửa:** `Room.Leave/Kick/Ban` (dưới room scope) báo cho `Lobby` đóng room khi trống, ngay trong
cùng thao tác. Xem C10 về race hiện tại và QĐ-7 về room `!mp make`.

**A8 [C] Countdown nằm ngoài room, có khóa riêng.**
`RoomCountdowns` giữ `Dictionary<int, Countdown>` và `Lock` riêng (`RoomCountdowns.cs:14`). Đây là
cơ chế đồng bộ thứ hai cho state của room (`AGENTS.md` cấm). Countdown còn định danh room bằng
`int` (xem C6).
**Sửa:** `Room.Countdown` là state của room, đổi dưới room scope, phát `CountdownStarted`,
`CountdownCancelled`, `CountdownElapsed` (và tick nếu cần hook). Hết giờ thì room tự `Start()`.
Bỏ `RoomCountdowns`.

**A9 [T] Gateway tự cho session join các kênh auto-join.**
`Gateway.cs:76–81`. Luật auto-join là của danh mục kênh.
**Sửa:** khi có session mới, danh mục kênh tự cho session vào các kênh `AutoJoin` mà nó đọc được.
`Gateway` chỉ mở session.

**A10 [T] Gateway phân nhánh theo loại session để gỡ khỏi registry.**
`Gateway.cs:121–126` (`if IrcSession … else if GameSession`). Mỗi loại session mới lại thêm nhánh.
**Sửa:** một object nơi user online giữ mọi loại session (QĐ-2).

**A11 [T] Định danh bằng id thay vì object.**
`RoomCountdowns.Start/Cancel(int roomId)`, `IRoomRegistry.EnterAsync(int)`, `Remove(int)`,
`IChannelRegistry.Remove(string name)`, `Lobby.CloseRoomAsync(int)`,
`MatchFlowCommands.cs:86`. `AGENTS.md`: id chỉ để tra repository.
**Sửa:** nhận `Room`/`ChannelSession`. Id chỉ còn ở biên protocol (`!mp join <id>`, packet).

**A12 [T] "1 thao tác = 1 event" bị vi phạm.**
- `!mp set`: `TeamType`, `WinCondition`, `Resize` = 3 event (`MatchFlowCommands.cs:62–71`).
- `!mp mods X`: `Freemods = false` rồi `Mods = …` = 2 event (`:42`, `:50`).
- `Lobby.CreateRoomAsync`: gán `Password` phát `RoomPasswordChanged` cho room chưa đăng ký
  (`Lobby.cs:45`).
- `!mp makeprivate`: tạo room rồi mới `IsVisible = false` (`RoomLifecycleCommands.cs:31`).

**Sửa:** thao tác gộp cần một điểm vào trên `Room` (ví dụ gán `MatchSettings` một lần, phát một
event), và room tạo ra đã mang sẵn password và visibility.

---

## 3. [B] Việc của Infrastructure/Host đang nằm trong Application

**B1 [N] Event handler nằm trong Application.**
`SessionEventHandlers`, `ChannelEventHandlers`, `RoomEventHandlers`, `RoundEventHandlers` và phần
đăng ký trong `DependencyInjection.cs:39` trở đi.
**Sửa:** xóa khỏi Application. Hành vi của chúng chuyển sang Infrastructure/Host (gửi packet, ghi
`MatchEvent`, lưu `Round`). Luật môi trường bên trong (A7) phải chuyển vào Room/Lobby **trước**.

**B2 [C] Interface `IEventHandler`/`IEventDispatcher` nằm trong Application.**
Application chỉ cần `Event` và `IEventPublisher<T>`. Cơ chế tiêu thụ là của nơi tiêu thụ.
**Sửa:** chuyển hai interface sang Infrastructure (QĐ-6).

**B3 [N] Application tự đẩy notification cho client.**
- Cả hệ `Notification` (`Common/Notifications`, `PresenceNotifications`, `SpectatorNotifications`,
  `ChannelNotifications`, `ChatDeliveryNotifications`, `RoomNotifications`).
- `IClientConnection`, `UserSession.Connection`, `UserSession.Notify` (`UserSession.cs:33`, `:49`).
- Các nơi gọi `Notify` trực tiếp ngoài handler: `Messaging` (mọi tin nhắn),
  `RoomCountdowns.cs:87`, `ScoreSubmission.cs:69`.

Chọn gửi gì cho ai là quyết định của transport. Bancho, IRC và SSE mỗi bên chọn khác nhau.
**Sửa:** xóa `Notification` và `IClientConnection` khỏi Application. Mọi chỗ đang `Notify` phải
phát event tương ứng (D). Host giữ ánh xạ session ↔ connection.

**B4 [C] Bot và `!mp` được nối thẳng vào `Messaging`.**
`Messaging` gọi `ChatCommands.ExecuteAsync` sau mỗi tin (`Messaging.cs:53`, `:77`), rồi tự dựng câu
trả lời của BasilBot. Toàn bộ phần phân tích text lệnh (`ChatCommands`, `MpCommands`,
`*Commands`), chuỗi trả lời (`BotReplies`, `MpReplies`), `ILocalizer` đều là giao diện người dùng.
**Sửa:** BasilBot là một tác nhân tích hợp. Nó nghe `MessagePosted`, phân tích lệnh, gọi thao tác
của Application trên `Room`/`Lobby`, rồi `Post` câu trả lời vào kênh như mọi user khác. Chuyển
lệnh, chuỗi trả lời, localizer ra ngoài Application (Infrastructure hoặc một project bot riêng).
Application chỉ giữ các thao tác mà lệnh gọi tới.

**B5 [T] Persistence xen vào thao tác môi trường, không nhất quán.**
- `Lobby.CloseRoomAsync` lưu `Match` (`Lobby.cs:65`).
- `MakeAsync` lưu `Match` (`RoomLifecycleCommands.cs:32`).
- Handler lưu `Round` và `MatchEvent`.
- Đổi tên room (`Room.Name`) **không** được lưu ở đâu cả.

**Sửa:** chốt một luật (QĐ-8). Đề xuất: môi trường phát event, Infrastructure lưu từ event. Riêng
chỗ cần id do nơi lưu cấp (tạo `Match`) vẫn gọi repository lúc tạo.

**B6 [T] Cấu hình của Host/Infrastructure nằm trong Application.**
- `StorageConstants` (đường dẫn `Data/Basil.db`, `Replays`, `Faqs`, …).
- `BasilOptions.HostOptions` (Bancho domain, port, certificate, IRC port).
- `UpdateOptions`.
- `BasilSettings` (MOTD, menu icon, mirror, admin key).

Trong Application chỉ `BotOptions.Prefix` được đọc (`ChatCommands.cs:39`), và nó theo bot ra ngoài
(B4). Tên `IConfiguration` trùng `Microsoft.Extensions.Configuration.IConfiguration`.
`BasilSettings` và `BasilOptions` cùng section `"Basil"` (`BasilSettings.cs:16`,
`BasilOptions.cs:11`).
**Sửa:** chuyển cả `Common/Configuration` về nơi dùng.

**B7 [T] Parser cú pháp truy vấn dạng text nằm trong Application.**
`BeatmapQuery.Parser`, `BeatmapsetQuery.Parser`, `UserQuery.Parser` phân tích cú pháp của
osu!direct và API. Đó là giao diện nhập. Ba parser còn chép lại cùng `Unquote`, `TokenPattern`,
`CollapseWhitespace`.
**Sửa:** giữ record truy vấn có cấu trúc trong Application. Chuyển parser sang Host (một bộ tách
token dùng chung).

**B8 [C] Registry trạng thái runtime là port.**
`ISessionRegistry`, `IChannelRegistry`, `IRoomRegistry` + `IRoomScope`. Đây là state trong bộ nhớ
của chính môi trường, cộng khóa bảo vệ bất biến của `Room`, chứ không phải I/O. Để ở port khiến
Application không tự vận hành được môi trường của mình. Bất biến đồng thời của room phụ thuộc vào
một implementation bên ngoài (hiện chưa tồn tại).
**Sửa:** thành class cụ thể trong Application, là object môi trường phát event (bảng mục 1). Port
chỉ dành cho thứ ngoài process: DB, file, mạng (QĐ-2).

**B9 [T] Port beatmap lộ hệ thống file.**
`IBeatmapAnalyser.Analyze(string beatmapFilePath, …)` (`IBeatmapAnalyser.cs:21`).
`BeatmapCatalog.ImportAsync` nhận `FilePath` lẫn `Content` cho mỗi difficulty
(`BeatmapCatalog.cs:27`).
**Sửa:** analyser nhận nội dung (`Stream`/bytes). Bỏ `FilePath`.

**B10 [Th] Port và type chưa dùng trong Application.**
`IMirrorClient` (+ `MirrorSearch`, `MirrorBeatmapset`, `MirrorBeatmap`), `IPasswordHasher`,
`RegisterAttempt`, `ChannelQuery`.
**Sửa:** chuyển về nơi dùng, hoặc xóa khi Application có thao tác dùng tới (ví dụ đăng ký).

---

## 4. [C] Bug chức năng

**C1 [N] Round không bao giờ kết thúc.**
`PlayerLoaded`, `AllPlayersLoaded`, `PlayerSkipped`, `AllPlayersSkipped`, `PlayerFailed`,
`PlayerCompleted`, `RoundCompleted` được khai báo nhưng không nơi nào phát (grep chỉ thấy trong
handler). Chỉ `Room.Abort` gán `EndedAt` (`Room.cs:378`). Sau `Start()` lần đầu, `InProgress` đúng
mãi. `!mp start` lần hai trả "match already in progress", trừ khi có người `!mp abort`.
`RoomSlot.Status` và `IntroSkipped` là setter công khai, không gắn với tiến trình round.
**Sửa:** `Room` mô phỏng tiến trình round: người chơi báo loaded/skip/fail/complete; room tự phát
`AllPlayersLoaded`, `AllPlayersSkipped`. Khi mọi người đang chơi đã xong, room đặt `EndedAt`, đưa
slot về `NotReady` và phát `RoundCompleted`.

**C2 [N] Chat trong room không tới được.**
`RoomChannel.Name` là `mp_{MatchId}`, không có `#` (`RoomChannel.cs:8`). `Messaging.SendAsync` chỉ
coi đích bắt đầu bằng `#` là kênh (`Messaging.cs:34`). Gửi tới `mp_5` bị hiểu là PM tới user tên
`mp_5`, rồi bị bỏ im lặng. Client osu! gửi tới `#multiplayer`, cũng không có ánh xạ nào trong cả
`src/`.
**Sửa:** đích gửi tin là object kênh/hội thoại, không phải chuỗi tên. Host tự dịch
`#multiplayer` → kênh của room chứa người gửi. Application không đoán theo ký tự đầu.

**C3 [N] Referee qua IRC hoặc không ngồi trong room không dùng được `!mp`.**
`MpCommands.cs:66`: room lấy từ slot của người gửi (`sender is not GameSession { Room: … }`).
`ChatCommands` nhận `channel` nhưng không truyền cho `MpCommands` (`ChatCommands.cs:55`). Referee
tournament thường ở IRC và không ngồi slot. Người đó chỉ dùng được `make` và `join`, mà `join` lại
đòi `GameSession`. Cũng trái nguyên tắc "referee, host, creator là ba khái niệm tách biệt".
**Sửa:** room xác định theo kênh nơi lệnh được gửi (kênh của room); quyền theo `Room.IsReferee`.

**C4 [C] `!mp banlist` luôn rỗng.**
`RefereeCommands.cs:75–80` liệt kê người **đang ngồi** và bị ban. Nhưng `Ban` đẩy người đó ra khỏi
slot, và `Seat` chặn người bị ban.
**Sửa:** liệt kê `room.Banned`.

**C5 [N] `!mp move` vào slot có người làm hỏng state.**
`RoomSlot.MoveTo` chỉ kiểm tra slot đích bị khóa, không kiểm tra có người (`RoomSlot.cs:196–204`).
Người ở slot đích bị ghi đè. `GameSession.Slot` của họ vẫn trỏ vào slot đó. Họ biến mất khỏi danh
sách slot nhưng vẫn tưởng mình đang ngồi.
**Sửa:** từ chối khi slot đích có người (bancho.py: "slot không trống"), hoặc hoán đổi nếu muốn
(QĐ-9).

**C6 [C] Countdown gắn với room id có thể bị dùng lại.**
`Lobby.FreeRoomId` lấy id trống nhỏ nhất. `CloseRoomAsync` không hủy countdown. Kịch bản:
`!mp start 30`, `!mp close`, trong 30 giây có room mới nhận cùng id. Room mới tự start nếu đã chọn
map.
Ngoài ra:
- `!mp start` (không đếm) và `!mp abort` không hủy countdown đang chạy.
- `Countdown` nuốt exception vì `RoomCountdowns` không truyền `OnException` (`Countdown.cs:148`).

**Sửa:** xem A8. Countdown thuộc room nên chết cùng room.

**C7 [C] Người đã rời, bị kick, bị ban vẫn ở kênh room.**
Hậu quả của A6: họ vẫn đọc và gửi được chat room.

**C8 [C] (cần xác nhận phía Host) `HostTransferred` gửi cho cả room, không có payload.**
`RoomEventHandlers.cs:85`. Nếu Bancho ánh xạ nó thành `MatchTransferHost`, **mọi** client sẽ tưởng
mình là host. Những người khác lại không nhận `RoomUpdated` cho việc đổi host.
**Sửa:** sẽ tự biến mất khi B1/B3 xong. Khi viết lại ở Host: chỉ host mới nhận transfer, cả room
nhận update.

**C9 [T] Lời mời ghi sai người mời.**
`PlayerInvited` không mang người mời. Handler dùng `Room.Creator ?? invited.Player`
(`RoomEventHandlers.cs:112`). Room không có creator thì người được mời thấy chính mình là người
mời.
**Sửa:** `Room.Invite(by, user)`, và event mang cả hai.

**C10 [C] Race khi tự đóng room.**
Handler kiểm tra "room trống" ngoài room scope, rồi `CloseRoomAsync` đóng **vô điều kiện**. Người
vừa vào giữa hai bước bị đẩy ra.
Ngoài ra, room `!mp make` bị đóng khi referee kick người cuối, dù referee vẫn đang điều khiển.
**Sửa:** A7 (kiểm tra và đóng trong cùng scope) và QĐ-7.

**C11 [T] Đóng room giữa round thì round không bao giờ kết thúc.**
`Room.Close` (`Room.cs:395`) không gán `EndedAt` cho round đang chạy, và không phát event round.
**Sửa:** `Close` kết thúc round đang chạy (abort) như một phần của cùng thao tác.

**C12 [T] Host rời room thì room mất host, không chuyển.**
`RoomSlots.Vacate` gán host null im lặng (`RoomSlots.cs:115`). Room còn người nhưng không ai là
host, nên không ai start được từ client. bancho.py chuyển host cho người kế tiếp. QĐ-10.

**C13 [T] Tạo room: event và kết quả bị bỏ qua.**
- `Lobby.CreateRoomAsync` gán password trong vòng lặp thử id, phát event trên các `Room` có thể bị
  vứt đi (`Lobby.cs:45`).
- Kết quả `channels.TryAdd(room.Channel)` bị bỏ qua (`:48`).
- `Match` được insert trước khi có room id: hết id thì còn match mồ côi (đã biết trong plan
  identity-wrapper).

**C14 [C] Đăng nhập lại bị chặn khi phiên cũ còn treo.**
`Gateway.cs:68` trả `AlreadyOnline`. Client crash thì không vào lại được cho tới khi phiên cũ bị
dọn. QĐ-11.

**C15 [T] Tin nhắn riêng.**
- Chỉ tìm người nhận trong `GameSession` (`Messaging.cs:61`), nên user IRC không nhận được PM.
- Tìm bằng quét tuyến tính và so tên.
- Gửi tới người offline bị bỏ im lặng.
- Lệnh bot chạy trong PM tới **bất kỳ** ai (`:77`), rồi câu trả lời của bot bị chèn vào hội thoại
  của hai người.

**Sửa:** hội thoại là object (bảng mục 1). Bot chỉ phản hồi khi PM tới bot hoặc trong kênh (B4).

**C16 [C] Bảo mật: tên FAQ do user nhập đi thẳng làm khóa storage.**
`ChatCommands.cs:93`: `faqStorage.OpenReadAsync(requested)` với text thô từ chat. Nếu
implementation ghép khóa thành đường dẫn file, một tên có `..` có thể đọc file ngoài thư mục FAQ.
Thư mục `Data/` chứa cả cấu hình và DB.
**Sửa:** chỉ mở các mục có trong danh sách FAQ đã biết (tra qua `faqSearch` hoặc một kiểu `Faq`
có định danh). Implementation storage vẫn phải tự chặn đường dẫn.

**C17 [T] `UserQuery`: parse privilege bị đảo.**
`UserQuery.Parser.cs:120`: `if (Enum.TryParse(...)) return false;`. Parse đúng thì token bị coi là
text. Parse sai thì filter bị gán giá trị mặc định.
**Sửa:** `if (!Enum.TryParse(...)) return false;`.

**C18 [Th] `BeatmapQuery.Parse` không gom khoảng trắng.**
`BeatmapQuery.Parser.cs:43`: `CollapseWhitespace` khai báo nhưng không dùng (warning CS8321).
`Keywords` có thể là `""` hoặc có khoảng trắng thừa thay vì `null`.

**C19 [T] `BeatmapsetQuery.Parse` ném exception với ngày sai.**
`created=2024-13` hoặc `created=2024-02-31` khớp regex, rồi `new DateTimeOffset` ném
`ArgumentOutOfRangeException` (`BeatmapsetQuery.Parser.cs:181`). Đáng lẽ phải lùi về text như luật
"giảm cấp nhẹ nhàng".

**C20 [T] `ScoreSubmission`.**
- Cộng stats theo kiểu đọc–sửa–ghi không khóa (`ScoreSubmission.cs:60`): hai lần nộp đồng thời mất
  một lần cộng.
- Đọc `session.Room?.LastRound` ngoài room scope (`:46`).
- Người chơi rời room trước khi score tới thì score không gắn round.
- `PlayCount` chỉ tăng với lượt pass (QĐ-12).

**C21 [T] `BeatmapCatalog.ImportAsync`.**
- Lưu metadata trước, archive sau (`BeatmapCatalog.cs:39`, `:59`). Lỗi giữa chừng để lại set không
  có file.
- Difficulty bị bỏ khỏi set trong bản upload mới không bị xóa.

**C22 [T] `!mp mods` phân biệt hoa thường.**
`"None"`/`"Freemod"` phân biệt hoa thường (`MatchFlowCommands.cs:41`). `!mp mods freemod` bị parse
thành chuỗi mod.

**C23 [T] Dùng exception để chọn câu trả lời, và trả lời sai.**
`SlotCommands.cs:41`, `:83`: bắt `InvalidOperationException`. `!mp team` trả "cách dùng" cả khi
người chơi đang chơi hoặc room không dùng team.
**Sửa:** thao tác của room trả kết quả có kiểu (A5).

**C24 [C] `!mp lock` không có tác dụng trong Application.**
`RoomSlots.Locked` chỉ được ghi (`RoomSettingsCommands.cs:35`), không nơi nào đọc. Application
không có thao tác người chơi tự đổi slot, team, mod để luật khóa chặn lại.
**Sửa:** thêm thao tác người chơi trên `Room` (đổi slot, đổi team, ready, đổi mod) và cho room
lock chặn chúng (xem D).

**C25 [T] Chuyển trạng thái slot tùy ý.**
`RoomSlot.Status` cho gán `Complete` khi chưa `Playing`, gán `Playing` ngoài round.
`IntroSkipped` đổi mà không phát event (`RoomSlot.cs:49`, `:69`).
**Sửa:** C1. Trạng thái trong round do room điều khiển, không mở setter công khai.

**C26 [C] Channel event không bao giờ đóng, và không giới hạn.**
Mọi `Channel<T>` là `CreateUnbounded` và không bao giờ `Writer.Complete()`.
- Nếu chưa có ai đọc (ví dụ event của session IRC), bộ nhớ tăng mãi.
- Khi room hoặc session kết thúc, vòng `await foreach` của nơi tiêu thụ không bao giờ thoát, nên
  task bị rò.

**Sửa:** object kết thúc (room đóng, session ngắt, kênh đóng) thì complete writer.

**C27 [C] Bảo mật: ai cũng đọc được kênh của mọi room.**
`RoomChannel.ReadPrivilege = Player` (`Multiplayer/RoomChannel.cs`). Referee vào kênh room bằng
`/join #mp_<id>` qua IRC [osu-wiki], nên kênh room phải join được theo tên. Kết hợp hai điều này,
bất kỳ người chơi nào cũng `/join` được kênh của mọi room, kể cả room có password, và đọc chat.
**Sửa:** room quyết định ai đọc được kênh của nó (người ngồi, referee, creator, observer).

**C28 [T] Silence không đưa người chơi ra khỏi room.**
[bancho.py] silence thì người đó rời match. Application không có luật này.

**C29 [T] `!mp makeprivate` sai nghĩa.**
[osu-wiki]: room riêng tư nghĩa là **lịch sử trận** chỉ creator và người tham gia xem được. Basil
dùng `IsVisible = false` để **chặn vào room** trừ người được mời/referee
(`RoomLifecycleCommands.JoinAsync`). Xem R8 trong kế hoạch.

**C30 [N] Mất tính năng osu!tourney đăng nhập nhiều phiên.**
Basil cũ (`Infrastructure/Auth/LoginService.cs:117`) và [bancho.py] cho một tài khoản nhiều phiên
khi client stream là `Tourney` (cần quyền donator, không bị restrict). `Gateway` mới trả
`AlreadyOnline` cho mọi phiên game thứ hai (`Gateway.cs:68`), và `UserSession` định danh theo
`(kiểu, User)`. osu!tourney mở nhiều client cùng lúc cho một trận, nên tính năng lõi của server
tournament bị hỏng. Xem R16 trong kế hoạch.

---

## 5. [D] Thiếu điểm hook (event cần có mà chưa có)

Hiện nhiều việc xảy ra mà không có event, nên không layer nào cắm vào được (SSE, bot, ghi lịch sử,
thống kê):

- **D1** Session online/offline (`PlayerOffline` notification có nhưng không ai phát).
- **D2** Tin nhắn được gửi, trong kênh và PM (`MessagePosted`). Điều kiện cần cho bot (B4) và log
  chat.
- **D3** Room mở (`RoomOpened`). Thiếu nó, Infrastructure không biết có nguồn event mới để đọc
  (E1).
- **D4** Tiến trình round (C1) và `ScoreSubmitted` gắn với round/room.
- **D5** Countdown bắt đầu, hủy, hết giờ (A8).
- **D6** Kênh mở/đóng (kênh của room sinh ra và mất theo room).
- **D7** Beatmapset được import.
- **D8** Người chơi tự thao tác trong room: đổi slot, team, ready, mod, báo có/không có map. Hiện
  chỉ referee (qua `!mp`) mới có đường vào.

---

## 6. [E] Mô hình event

**E1 [C] Không có nguồn gốc để khám phá nguồn event mới.**
Mỗi `Room`, session, kênh có `Channel<T>` riêng, sinh ra lúc runtime. Nơi tiêu thụ chỉ biết tới
chúng nếu object môi trường sở hữu chúng (Lobby, nơi user online, danh mục kênh) phát event
"đã mở" (D1, D3, D6). QĐ-5: giữ kênh riêng từng object kèm event "đã mở", hay một luồng chung cho
cả môi trường.

**E2 [T] Event mang object sống, dễ thay đổi.**
`RoomSlot`, `Room`, `GameSession` trong event. Nơi tiêu thụ bất đồng bộ đọc state **lúc đọc**,
không phải lúc phát. Ví dụ `PlayerKicked.Slot` có thể đã có người khác ngồi. QĐ-13 (trước đây đã
chốt notification mang tham chiếu; với event bất đồng bộ cần cân nhắc lại).

**E3 [Th] Xếp category sai chỗ.**
`ChannelEvent`/`MemberJoined`/`MemberParted` nằm trong `Sessions/SessionEvents.cs:25`. `PlayerMoved`
là thay đổi slot nhưng nằm trong `RoomMembershipEvent`.

**E4 [Th] Tên event trùng tên notification.**
`RoundStarted`, `RoundAborted`, `RoundCompleted`, `AllPlayersLoaded`, `AllPlayersSkipped`,
`PlayerSkipped`, `PlayerFailed`, `RoomClosed`. Phải dùng alias
`using Notification = Basil.Application.Chat;`. Tự hết khi B3 xong.

**E5 [Th] Hai đường đổi state song song.**
Setter công khai phát event, cùng bộ `internal Set*` im lặng (`RoomSlot.cs:168` trở đi) và
`Room.SetHostSilently`. Chấp nhận được để gộp thành một event, nhưng hệ quả im lặng phải nằm trong
event mang theo. Ví dụ `PlayerLeft` phải cho biết host bị gỡ.

---

## 7. [F] Port, persistence, query

- **F1 [C]** `ICreatable<TData,TValue>` thừa so với `IRepository`: bỏ (QĐ-1). Việc này đảo bước 2
  và 4 của `plans/identity-wrapper-plan-20260930.md` (chưa commit).
- **F2 [T]** `UserSafeName`: bỏ khỏi Application. `IRepository<string, User>` nhận tên thô,
  repository tự chuẩn hóa khi tra. Nơi gọi: `Gateway.cs`, `SlotCommands.cs:24`, `ChatCommands`.
- **F3 [T]** `ISearchable<TValue>.SearchAsync(Query<TValue>)` (`ISearchable.cs:20`) nhận mọi loại
  query của cùng kiểu giá trị. Implementation phải tự đoán kiểu query.
  **Sửa:** `ISearchable<TQuery, TValue> where TQuery : Query<TValue>`.
- **F4 [T]** `SortOptions(string Column)` (`SortOptions.cs:3`) lộ tên cột lưu trữ.
  **Sửa:** mỗi query có enum khóa sắp xếp riêng.
- **F5 [T]** `MatchEvent` lưu qua `IRepository<int, MatchEvent>`. Đây là nhật ký chỉ thêm, không
  có khóa, nên không khớp ngữ nghĩa "ghi đè theo khóa". Khi B1 xong, nhật ký này có thể do
  Infrastructure ghi thẳng từ event room.
- **F6 [Th]** FAQ không có kiểu. Nó là `string` ở ba nơi: `FaqQuery : Query<string>`,
  `ISearchable<string>`, `IStorage<string>`. Liên quan C16.
- **F7 [Th]** `BeatmapQuery.Md5` là `IReadOnlyCollection<string>` (`BeatmapQuery.cs:45`). Trái luật
  "không bao giờ dùng `string` cho MD5".
- **F8 [Th]** `QueryOptions` không kiểm tra `Offset`/`Amount` âm.
  `ComparisonOperatorExtensions.Parse` không phải extension method.

---

## 8. [H] Chất lượng nhỏ

- `16` viết cứng thay vì `RoomSlots.MaxSlotCount`: `MatchFlowCommands.cs` (`Set`),
  `RoomSettingsCommands.cs:73`, `SlotCommands.cs` (`MoveAsync`).
- Chuỗi hiển thị viết cứng, trái rule 9:
  - `"not private"`/`"private"` (`RoomSettingsCommands.cs:43`)
  - định dạng map `"{artist} - {title} [{version}]"` (`:20`)
  - `!roll` (`ChatCommands.cs:66`); hằng `BotReplies.RollResult` có mà không dùng
  - `HelpText` của `ChatCommands` và `MpCommands`

  Sẽ ra ngoài cùng bot (B4).
- `Random.Shared` trong `!roll` là ngẫu nhiên không kiểm soát, không test xác định được.
- `LoginResult` (Users) tham chiếu `GameSession` (Sessions), nên Users phụ thuộc Sessions.
  **Sửa:** chuyển `LoginResult` vào Sessions (hoặc cạnh object nơi user online).
- `UserQuery.cs:4` import `Basil.Application.Beatmaps` không cần thiết.
- `Lobby.Observers` trả về room, tên sai nghĩa và chưa dùng.
- `Room.Settings { private get; init; }` (`Room.cs:30`): getter private, init public, khó hiểu.
- `Room.Password` getter private nhưng `Room.Url` (`Room.cs:246`) lộ password ra public, và
  `RoomPasswordChanged` mang password.
- `RoomSlot.ThrowIfEmpty/ThrowIfLocked/ThrowIfPlaying/ThrowIfDifferentRoom` là guard nội bộ nhưng
  để public.
- `RoomSlots.Move` là static nhận hai slot. Nên là thao tác của room trên người chơi.
- `RoomSlots.Room`, `RoomSlot.Slots`, `RoomSlot.Index` là public field, không phải property.
- `RefereeCommands` phụ thuộc `SlotCommands` chỉ để dùng `ResolveAsync`.
- `ListReferees` không liệt kê creator, dù `IsReferee` tính cả creator.
- `!mp aborttimer` luôn báo "đã hủy" kể cả khi không có countdown, dù hằng
  `MpReplies.NoCountdownRunning` có sẵn mà không dùng.
- `Gateway.cs:50` bắt `ArgumentException` cho MD5 sai định dạng. Nhưng `PasswordHash` đã là kiểu
  `Md5`, nên không thể xảy ra (rule 2 của `AGENTS.md`).
- `UserSession.LastActiveAt` chỉ được ghi, không ai đọc trong Application. `IClientConnection.IsOpen`
  chưa dùng.
- `ChannelSession` là kênh chat đang sống, không phải "session". Tên dễ nhầm với `UserSession`.
- `BotReplies.cs` import `Basil.Application.Multiplayer` không dùng. `MpReplies.cs` import
  `Basil.Application.Chat` không dùng.
- Hằng trong `MpReplies` chưa dùng: `CreateFailed`, `SettingsBeatmapNotFound`,
  `NoCountdownRunning`.
- Notification chưa dùng trong Application: `Announcement`, `PlayerOffline`, `RoomJoined`,
  `RoomJoinRefused`, `RefusalReason.RecipientAway`, `RefusalReason.RecipientRestrictsMessages`.

---

## 9. Quyết định cần người dùng chốt

**Cập nhật 2026-09-30.** Người dùng đã trả lời:

- Q1: viết thẳng contract `IXxxRepository`. Chỉ trừu tượng thêm interface chung khi các repository
  có cùng thao tác, vẫn giữ `IXxxRepository`.
- Q2: có.
- Còn lại: "hầu hết đồng ý".
- C2: tên kênh giữ không có `#`, transport tự thêm.

Nghiên cứu bancho.py và osu-wiki mở lại Q7, Q11, Q12 và thêm câu hỏi mới (R1–R27). Người dùng đã
chốt tất cả; bảng quyết định cuối cùng nằm ở `plans/application-environment-plan-20260930.md`
phần A; việc cần làm xếp theo phase ở phần C (phụ lục D nối từng mục của review với phase). Những chỗ khác đề xuất bên dưới:

- Q7: room tournament trống đóng sau **15 phút** (không phải "không bao giờ").
- Q11: như bancho.py — phiên cũ im từ 10 giây trở lên thì thay, còn hoạt động thì từ chối.
- Người tạo room luôn là creator (trong game, `!mp make`, hoặc API nếu được chỉ định), quyền cao
  hơn referee và được ghi nhớ trên match. `!mp` chỉ dành cho creator và referee; host không có
  quyền `!mp` chỉ vì là host.
- "Riêng tư" theo nghĩa osu!: lịch sử trận riêng tư (`IsPrivate`), không chặn người vào room.
- Room được chọn map server không có.

Kế hoạch đó thay thế mục 10 bên dưới.

1. **Thay `ICreatable` bằng gì.** (a) Thêm `AddAsync(TData) → TValue` vào chính `IRepository` cho
   loại có id do nơi lưu cấp. (b) `SaveAsync` của repository trả lại item đã lưu. Đề xuất (a): một
   port, hai thao tác rõ nghĩa.
2. **Registry thành object môi trường trong Application.** Nơi user online, danh mục kênh, Lobby là
   class cụ thể, phát event mở/đóng, Lobby giữ khóa room. Đề xuất: có. Trong `src/Basil.Infrastructure` hiện
   chỉ có `InMemoryChannelRegistry` (viết theo namespace cũ). `ISessionRegistry` và `IRoomRegistry`
   chưa có implementation, nên đổi bây giờ rẻ.
3. **Ai phát `StatusChanged`.** Session không được phát event. Đề xuất: object nơi user online
   (`Presence`) sở hữu trạng thái hiển thị của từng session và phát event.
4. **Luồng spectate sống ở đâu.** Đề xuất: object nơi user online giữ một luồng spectate cho mỗi
   người đang được xem, tạo khi có người xem đầu tiên.
5. **Một luồng event chung cho cả môi trường, hay kênh riêng từng object kèm event "đã mở".** Đề
   xuất: kênh riêng từng object (giữ quyết định cũ), cộng event "đã mở"/"đã đóng" từ object sở hữu,
   và complete writer khi đóng.
6. **`IEventHandler`/`IEventDispatcher` chuyển hẳn sang Infrastructure?** Đề xuất: có. Application
   chỉ giữ `Event` và `IEventPublisher<T>`.
7. **Room tạo bằng `!mp make` có tự đóng khi trống không.** bancho.py giữ room tournament tới khi
   `!mp close`. Đề xuất: room có creator thì không tự đóng.
8. **Luật persistence.** Đề xuất: môi trường phát event, Infrastructure lưu từ event. Ngoại lệ duy
   nhất là tạo item cần id do nơi lưu cấp.
9. **`!mp move` vào slot có người:** từ chối hay hoán đổi. Đề xuất: từ chối (giống bancho.py).
10. **Host rời room:** chuyển host cho người kế tiếp, hay để trống. Đề xuất: chuyển (giống osu!).
11. **Đăng nhập khi đã online:** từ chối (hiện tại) hay đá phiên cũ. Đề xuất: đá phiên cũ.
12. **`PlayCount` có tính lượt fail không.**
13. **Event mang tham chiếu object sống, hay giá trị chụp lúc phát** (user, số slot, trạng thái).
    Đề xuất: mang giá trị cho phần có thể đổi, tham chiếu cho định danh (`Room`, `User`).
14. **BasilBot và lệnh chat đi đâu:** Infrastructure hay project riêng (ví dụ `Basil.Bot`).

---

## 10. Thứ tự xử lý đề xuất

Mỗi bước build xanh `dotnet build src/Basil.Application/Basil.Application.csproj`.

1. Chốt mục 9.
2. **Dọn phần không thuộc Application** (B1, B2, B3, B6, B7, B10): xóa handler, notification,
   connection, cấu hình, parser. Chỗ đang `Notify` tạm thời để trống, và ghi lại event sẽ thay
   thế.
3. **Đảo quan hệ về phía bị tác động** (A1–A4, A9–A11): kênh sở hữu member; room sở hữu người chơi
   và trả lời "room của X"; luồng spectate; object nơi user online. Registry thành object môi
   trường (B8).
4. **Room mô phỏng đầy đủ:**
   - luật vào room (A5)
   - đồng bộ kênh (A6)
   - tự đóng (A7)
   - countdown (A8)
   - tiến trình round (C1, C25)
   - thao tác người chơi (C24, D8)
   - host rời (C12)
   - đóng giữa round (C11)
   - 1 thao tác = 1 event (A12)
   - event mở/đóng (D3, E1)
   - complete writer (C26)
5. **Tin nhắn:** kênh và hội thoại phát `MessagePosted` (C2, C15, D2). Bot ra ngoài, nghe event
   (B4, C3, C16).
6. **Port** (F1–F7, B9), score/beatmap (C20, C21, D4, D7).
7. **Bug nhỏ còn lại** (C17–C19, C22, mục 8).

Các bug C4, C5, C17, C19 sửa được ngay mà không chờ thiết kế lại, nếu cần gỡ chặn sớm.
