# Phân tầng Application: Services nằm dưới Objects, và Infrastructure tương xứng (2026-10-21)

Trạng thái (2026-10-21): **hướng đã được chủ dự án duyệt; các bước 1 và 3 đã làm xong, còn bước 2 (đảo tham chiếu) và
bước 4 (tài liệu, harness) — chờ lệnh.** Chi tiết ở mục 7.

## 1. Vì sao

`Application.Objects` hiện chứa cả khai báo service (`IAuthService`, `ISessionConnectionsService`, ...), nên "đối tượng"
và "dịch vụ" lẫn nhau, và service nhận hoặc tạo đối tượng (God Actor, thiết kế thủ tục quanh service). Hướng mới:

```text
Domain  <-  Application.Storage  <-  Application.Services (internal)  <-  Application.Objects (public)
                  ^                                                              ^
      Infrastructure.Storage / .Capabilities                          Infrastructure.Runtime, Hosts, Bot
```

* **Application.Storage**: cổng ra ngoài tiến trình. Gồm cổng bền (repository, byte storage, query record, paging) và cổng
  capability (`IBeatmapsetReader`, `IBeatmapAnalyser`, `IBeatmapAssets`, `IBeatmapsetMirror`, `IScreenshotInspector`)
  cùng kiểu dữ liệu của chúng. Nghĩa của "Storage" rộng ra thành "cổng ra ngoài".
* **Application.Services** (mọi kiểu `internal`): logic ứng dụng **không biết đối tượng sống, không phát event, không giữ
  trạng thái chạy**. Nhận và trả record Domain, kết quả, đầu vào đơn giản; gọi cổng của Storage. Chỉ Objects dùng nó
  (`InternalsVisibleTo Basil.Application.Objects`).
* **Application.Objects** (public): đối tượng sống, scope, context, event, cổng publish/handler, registry, và **mọi
  điều phối giữa nhiều đối tượng**. Objects tham chiếu Services và Storage. Objects có `AddApplication()` công khai, nơi
  duy nhất dựng context và đối tượng gốc.
* **Quy tắc event**: Services không phát event (event và `IEventPublisher<T>` nằm ở Objects). Service trả kết quả, Objects
  phát event.
* **Quy tắc God Actor**: mỗi điều phối nằm trên đối tượng mà nó tác động, không dồn vào `Server`.

Registry (`UserRegistry`, `GeneralChannelRegistry`) ở lại Objects, vì chúng giữ đối tượng sống. Nếu chủ dự án muốn
registry ở Storage thì phải đảo lại quyết định này.

## 2. Bảng từng file của `Application/Services`

| File hiện tại | Đích | Ghi chú |
|---|---|---|
| `Anticheat/IAnticheatService`, `AnticheatService` | **Xoá**; `BanchoConnection.Report(flags)` (Objects/Sessions) | Hành vi của chính kết nối người chơi, không cần quyền. Cần `RoomOf` qua cổng lobby của `SessionContext` và `IEventPublisher<AnticheatEvent>` |
| `Beatmaps/BeatmapsetService` (784 dòng) | Tách đôi. Logic nhập/quét/xoá (không đối tượng, không event) ở lại Services thành `BeatmapsetIndexer` (internal); đối tượng gốc `Beatmapsets` (Objects/Beatmaps) gọi nó và phát `BeatmapsetImported/Deleted` | `Of(record)`, `ImportAsync`, `ScanAsync()`, `ScanAsync(entry)` chuyển sang `Beatmapsets`. Indexer trả `(Imported, Deleted, Failures)` đủ để Objects phát event |
| `Chat/ChannelService` | **Xoá**; `GeneralChannelRegistry.JoinAutoChannels(by)` và `PartAll(by)` | Nhận connection chính nó, không cần quyền (thao tác hệ thống ghi rõ trong XML) |
| `Chat/GeneralChannelRegistry` | Chuyển sang Objects/Chat, kiểu `public sealed class GeneralChannelRegistry` (bỏ `IGeneralChannelRegistry`) | Giữ `Add/Remove` `internal` |
| `Common/Paging` | Chuyển sang `Application.Storage/Common` | Cả Services lẫn Objects dùng |
| `DependencyInjection` | Hai nửa: `AddApplicationServices()` **internal** (đăng ký các service còn lại) ở Services; `AddApplication()` **public** ở Objects (context, registry, đối tượng gốc, gọi nửa kia) | Hosts, harness DI và Bot chỉ gọi `AddApplication()` |
| `Multiplayer/MatchService` (`CloseUnfinishedAsync`) | **Xoá**; `Lobby.CloseUnfinishedAsync()` (thao tác hệ thống, startup) | `LobbyContext` thêm `IRoundRepository` và `IMatchEventRepository` |
| `Multiplayer/RoomService`, `Rooms/RoomMembershipService` | **Xoá**; `Lobby.ReleaseAsync(connection)`, `Lobby.RemoveSuspendedAsync(user)`, `Lobby.RemoveRefereeAsync(user)` | Duyệt mọi room là việc của lobby. Thao tác hệ thống, ghi rõ không cần quyền |
| `Multiplayer/Rooms/RoomRoundsService`, `RoomScope` | **Xoá**; `Room.RecordScoreAsync(RoundScore, UserRecord)` thao tác hệ thống (handler Runtime gọi) | Giữ khoá của room (`EnterAsync`) |
| `Scores/ScoreService` | Tách: kiểm tra + ghi (không đối tượng) ở Services thành `ScoreRecorder` (internal); `BanchoConnection.SubmitScoreAsync(...)` (Objects) tìm room/round/team qua cổng lobby, gọi recorder, phát `ScoreSubmitted` | Công thức MD5 giữ nguyên. Replay, stats, round-score nằm trong recorder |
| `Sessions/SessionConnectionsService` | **Xoá**; vòng đời kết nối chuyển vào `UserRegistry` (Open, Close, MarkActive, CloseIdle, CloseAll, CloseDisallowed, ConnectBotAsync) | Là chủ của người dùng online và chỉ mục token. Phát `UserConnectionOpened/Closed` ở đó |
| `Sessions/SessionService`, `ISessionService` | **Xoá** | Chỉ trả lại `Connections` |
| `Sessions/UserRegistry` | Chuyển sang Objects/Users (`UserRegistry` public sealed class, bỏ `IUserRegistry`) | Thêm `Of(UserRecord)`, `BlocksAsync` (cổng `IRoomUsersPort`), đăng nhập |
| `Users/AuthService` | Tách: kiểm đăng ký, xác thực mật khẩu, tạo tài khoản (record, không đối tượng) ở Services thành `AccountService` (internal); `UserRegistry.LoginAsync/RegisterAsync/CheckRegistrationAsync` và `UserRegistry.ScopeTo(by).CreateAccountAsync` (cần `UserManage`) trong Objects | Đăng nhập dựng connection và mở nó, nên thuộc chủ người dùng online |
| `Users/PermissionRules` | **Xoá** | Chỉ chuyển tiếp; dùng `user.Check(...)` trực tiếp |
| `Users/UserService` | **Xoá**; `UserRegistry.Of(record)` | |

Contract public bị bỏ hết: `IAuthService`, `IUserService`, `ISessionService`, `ISessionConnectionsService`,
`IChannelService`, `IMatchService`, `IRoomService`, `IRoomMembershipService`, `IRoomRoundsService`, `IScoreService`,
`IAnticheatService`, `IBeatmapsetService`, `IUserRegistry`, `IGeneralChannelRegistry`. Các cổng `internal` kiểu
`IRoomUsersPort`, `IUserConnectionsPort` ở lại nếu một đối tượng vẫn cần nói chuyện với đối tượng kia.

## 3. Bề mặt công khai sau đó (Hosts, Infrastructure.Runtime, Bot gọi)

| Gốc | Thành viên chính |
|---|---|
| `UserRegistry` | `Online`, `Find(token)`, `FindSpectating`, `Of(record)`, `LoginAsync`, `RegisterAsync`, `CheckRegistrationAsync`, `ScopeTo(by).CreateAccountAsync`, `Close(connection, reason)`, `MarkActive`, `CloseIdle`, `ConnectBotAsync` |
| `Lobby` | các thành viên hiện có, thêm `ReleaseAsync`, `RemoveSuspendedAsync`, `RemoveRefereeAsync`, `CloseUnfinishedAsync` |
| `Room` | thêm `RecordScoreAsync` (hệ thống) |
| `BanchoConnection` | thêm `SubmitScoreAsync`, `Report(flags)` |
| `GeneralChannelRegistry` | `All`, `JoinAutoChannels`, `PartAll` |
| `Beatmapsets` | `Of(record)`, `ImportAsync`, `ScanAsync()`, `ScanAsync(entry)` |
| `Server` | như hiện tại (+ `Notify`, `NotifyAll` ở phần Notify) |

## 4. Infrastructure tương xứng

| Project | Vai trò | Tham chiếu |
|---|---|---|
| `Infrastructure.Storage` | cài cổng bền (PostgreSQL, tệp, bộ nhớ); thư mục theo feature, chung gom `Database/`, `Files/`, `Memory/` | Domain, Application.Storage |
| `Infrastructure.Capabilities` (đổi tên từ `Infrastructure.Services`) | cài cổng capability: osu! rulesets, FFMpeg, mirror beatmap, ImageSharp | Domain, Application.Storage |
| `Infrastructure.Runtime` | event bus, handler, vòng nền, startup | Application.Objects |

* Không project Infrastructure nào tham chiếu `Application.Services`. Storage và Capabilities không tham chiếu
  `Application.Objects`.
* Mỗi project có một hàm `AddInfrastructureX()`; Host gọi `AddApplication()`, `AddInfrastructureStorage()`,
  `AddInfrastructureCapabilities()`, `AddInfrastructureRuntime()`; Bot thêm `AddBotApplication()`.

## 5. Thứ tự làm (mỗi bước build đủ 8 project + kiểm DI, rồi commit)

1. **Cổng capability và Paging sang `Application.Storage`**; đổi tên `Infrastructure.Services` → `Infrastructure.Capabilities`
   (thư mục, csproj, namespace, `.slnx`, DI `AddInfrastructureCapabilities`); `Infrastructure.Storage` gom `Database/`, `Files/`, `Memory/`.
2. **Đảo tham chiếu**: `Objects` → `Services` → `Storage`; Services internal + `InternalsVisibleTo`; tạo `AddApplication()` ở Objects.
3. **Chuyển từng nhóm** theo bảng mục 2 (người dùng/kết nối/đăng nhập; lobby/room/match; điểm/anticheat; beatmapset; chat),
   cập nhật Runtime và Bot theo gốc mới.
4. Cập nhật harness kiểm DI (gọi bốn hàm `Add…`), AGENTS.md, architecture.md, các plan.

## 6. Rủi ro đã biết

* Vòng DI: gốc nào cũng dựng bằng factory trong `AddApplication()` và giữ cổng, không giữ nhau trực tiếp; kiểm tra bằng
  harness với `timeout` (treo = có vòng).
* Mất kiểm tra quyền khi đổi chỗ: mọi thao tác hệ thống ghi rõ trong XML; entry point của người dùng chỉ qua `ScopeTo`.
* `BanchoConnection` cần cổng lobby và publisher; thêm vào `SessionContext`, không để kết nối giữ `Lobby` trực tiếp.

## 7. Tiến độ (2026-10-21)

Đã làm, đã build 8 project (Domain, Application.{Storage,Objects,Services}, Infrastructure.{Storage,Capabilities,Runtime},
Bot.Application) và kiểm DI (86 đăng ký, không vòng) sau mỗi bước:

| Việc | Kết quả | Commit |
|---|---|---|
| Cổng capability và `Paging` sang `Application.Storage`; `Infrastructure.Services` đổi thành `Capabilities` (chỉ tham chiếu Storage) | xong | `9a653975` |
| `Infrastructure.Storage` gom `Postgres/` (đổi từ `Database/` để tránh `Database.Database`), `Files/`, `Memory/` | xong | `44f5d8cf`, `8d3d4c0c` |
| Người dùng, kết nối, đăng nhập vào `UserRegistry` (`AccountService` object-free, cầu `IAccountService`) | xong | `d129ab9a` |
| Beatmapset: `Beatmapsets` (Objects) phát event, `BeatmapsetIndexer` object-free (cầu `IBeatmapsetIndexing`) | xong | `8416a3d0`, `606a8394` |
| Lobby/Room/Match và chat: `Lobby.ReleaseAsync/RemoveSuspendedAsync/RemoveRefereeAsync/CloseUnfinishedAsync`, `Room.RecordScoreAsync`, `GeneralChannelRegistry` | xong | `d56a275b` |
| Điểm và anticheat: `BanchoConnection.SubmitScoreAsync/Report`, `ScoreRecorder` object-free (cầu `IScoreRecording`); vòng dựng `UserRegistry` ↔ `Lobby` gỡ bằng cổng trì hoãn | xong | `c6e60679`, `3c3a007b` |

Khác với bảng mục 2, ghi lại để khỏi nhầm:

* `Beatmapsets` và các kiểu kết quả (`BeatmapsetImportResult`, `BeatmapsetScanResult`, `BeatmapsetScanOutcome`,
  `BeatmapsetIndexed`) nằm ở `Application.Storage/Beatmaps`, vì Objects công khai trả chúng và Services (nằm dưới) tạo ra chúng.
* `Anticheat/` và `Services/Multiplayer|Chat|Sessions` đã trống; `Services` còn `Users/AccountService`,
  `Beatmaps/BeatmapsetIndexer`, `Scores/ScoreRecorder` và `DependencyInjection`.
* Sự kiện giữ nguyên thứ tự phát nhưng `Beatmapsets.ScanAsync` phát `BeatmapsetImported/Deleted` sau khi cả lượt quét xong
  (trước đây phát giữa lượt quét, sau mỗi commit).

Còn lại (dừng chờ lệnh chủ dự án):

1. **Đảo tham chiếu**: `Application.Services` bỏ tham chiếu Objects (ba cầu tạm bị xoá, `ScoreOutcome`/kết quả của Services đưa
   về kiểu chung ở Storage), `Objects` tham chiếu `Services`; Services `internal` + `InternalsVisibleTo`;
   `AddApplication()` công khai ở Objects (context, đối tượng gốc, gọi `AddApplicationServices()` internal); harness DI gọi bốn
   hàm `Add…`.
2. **Tài liệu**: `docs/for-developers/architecture.md` và các tài liệu liên quan theo luật mới (AGENTS.md đã cập nhật).
3. **Câu hỏi mở** ở [`next-steps-20261021.md`](next-steps-20261021.md).
