# Kế hoạch: beatmapset đọc lười, spectating về session, service chia theo phần con (2026-10-13)

Chưa đụng vào code. Ba việc làm chung một lượt, theo thứ tự ở mục 4, mỗi phần một commit.

## 0. Đã chốt (phỏng vấn 2026-10-13)

| Câu hỏi                                    | Chốt                                                                                                   |
|--------------------------------------------|--------------------------------------------------------------------------------------------------------|
| Dạng facade                                | Khu vực là property của service cha, đối tượng vẫn là tham số: `rooms.Slots.MoveAsync(room, by, …)`    |
| Service nào chia                           | Luật chung: **cái gì có phần con bên trong thì chia**. Áp dụng: channels, sessions, users (và lobby, mục 3) |
| Spectating ở đâu                           | Con của `ISessionService`: `sessions.Spectators`                                                       |
| Quyền spectate                             | `TournamentObserveRooms` chứa `PlayerSpectate`; mọi client chỉ bị kiểm `PlayerSpectate`                 |
| Tên event spectate                         | `UserSpectatorJoined`, `UserSpectatorLeft`, `UserSpectatorFailed`                                       |
| Id do file .osu tự khai                    | Giữ: set/beatmap mới vẫn tạo dưới id file khai; caller không truyền id nữa                              |
| File mở lười                               | Lớp trừu tượng `BeatmapsetFile`                                                                         |
| Reader                                     | Vẫn là capability port (cần thư viện osu!); chỉ `BeatmapsetService` gọi                                 |
| Đầu vào thư mục                            | `DirectoryInfo`                                                                                         |

## 1. Beatmapset

### 1.1 `IBeatmapAnalyser` nhận `Stream`

```csharp
/// <param name="content">The .osu file, readable from its start; read only, not disposed.</param>
BeatmapAnalysis Analyze(Stream content, GameMode mode, GameMods mods);
```

- `OsuBeatmapAnalyser` bỏ `new MemoryStream(byte[])`; decoder lazer đọc qua `LineBufferedReader(stream, leaveOpen: true)`.
- Domain `Md5` thêm `static Task<Md5> ComputeAsync(Stream content, CancellationToken)` (`MD5.HashDataAsync`).

### 1.2 `IBeatmapAssets` chỉ còn thứ bên trong beatmap

- Bỏ `BeatmapsetAsset.Archive`, `ArchiveWithoutVideo` và nhánh `ExportAsync` trong `BeatmapAssets`.
- Asset = file .osu, background, audio, video, storyboard, và audio preview dựng từ audio.
- Tải nguyên .osz: host gọi thẳng `IBeatmapsetStorage.ExportAsync` (outer layers use storage directly).

### 1.3 `BeatmapsetFile` (Storage.Contracts/Beatmaps)

Đặt ở Storage.Contracts vì cả reader (Services.Contracts) lẫn storage dùng; Storage không tham chiếu ngược lên Services.

```csharp
/// <summary>One file of a beatmapset, opened anew each time it is read.</summary>
public abstract class BeatmapsetFile(string name, long length)
{
	/// <summary>Gets the file's name relative to the set, with '/' between folders.</summary>
	public string Name => name;

	/// <summary>Gets the file's size in bytes.</summary>
	public long Length => length;

	/// <summary>Opens the file's content from its start.</summary>
	/// <returns>A new stream that the caller disposes.</returns>
	public abstract Task<Stream> OpenAsync(CancellationToken cancellationToken = default);
}
```

Cài đặt `internal sealed` ở Infrastructure.Services: một lớp cho entry zip, một lớp cho file trên đĩa (tên cài đặt được nói cơ chế).

### 1.4 `IBeatmapsetReader` đọc archive và thư mục

```csharp
Task<BeatmapsetContent?> ReadAsync(Stream archive, CancellationToken cancellationToken = default);
Task<BeatmapsetContent?> ReadAsync(DirectoryInfo folder, CancellationToken cancellationToken = default);

/// <summary>What a beatmapset contains.</summary>
/// <remarks>Keeps its source open, so its files stay readable, until it is disposed.</remarks>
public sealed class BeatmapsetContent : IAsyncDisposable
{
	int? OnlineSetId; string Artist; string Title; string Creator;
	IReadOnlyList<BeatmapsetDifficulty> Difficulties;   // (int? OnlineId, string Version, GameMode Mode, BeatmapsetFile File)
	IReadOnlyList<BeatmapsetFile> Files;                // mọi file của set
}
```

- Reader mở từng .osu một lần để lấy metadata, version, mode; sau đó chỉ giữ `BeatmapsetFile`, không giữ `byte[]`.
- Với archive, `ZipArchive` sống tới khi `BeatmapsetContent` bị dispose. Archive không seek được: `ZipArchive` ở chế độ
  Read tự đệm vào bộ nhớ, reader không cần code riêng (kiểm lại khi làm).
- Giới hạn chống zip bomb (`MaxFiles`, tổng dung lượng, 16 MiB mỗi .osu) và kiểm tên file an toàn chuyển vào reader,
  vì reader đọc cả hai nguồn.
- Kiểm trước khi chốt thiết kế: một entry zip ở chế độ Read mở lại được nhiều lần (mỗi .osu mở khoảng 3 lần: hash,
  phân tích, lưu). Dùng API async của .NET 10 nếu có (`ZipArchive.CreateAsync`, `ZipArchiveEntry.OpenAsync`); tra
  Microsoft Learn trước.

### 1.5 `IBeatmapsetStorage.SaveAsync` nhận file

```csharp
/// <param name="files">The set's files; each stays readable until the call returns.</param>
Task SaveAsync(Beatmapset set, IReadOnlyCollection<BeatmapsetFile> files, CancellationToken cancellationToken = default);
```

Storage ghi từng file vào thư mục tạm rồi đổi tên; không giải nén zip nữa; vẫn giữ `SafePath`.

### 1.6 `IBeatmapsetService.ImportAsync`: hai nguồn, không id tùy chọn

```csharp
Task<BeatmapsetImportResult> ImportAsync(Stream archive, CancellationToken cancellationToken = default);
Task<BeatmapsetImportResult> ImportAsync(DirectoryInfo folder, CancellationToken cancellationToken = default);
```

- Set được chọn: set đã có beatmap trùng hash → set có id file khai (`OnlineSetId`) → set mới (tạo dưới id file khai
  khi có, như hiện nay).
- Hai overload đọc bằng reader rồi đi chung một luồng private: hash và phân tích từng difficulty (mở lại file), repository
  (set, beatmap, `RetainAsync`), storage `SaveAsync(files)`.
- Runtime: `BeatmapArchiveImporter` bỏ `IdFromFileName`. `BeatmapImportWatcher` lúc quét khởi động nhập cả thư mục con
  (set đã giải nén) qua overload `DirectoryInfo`; theo dõi trực tiếp vẫn chỉ `*.osz` (sự kiện tạo thư mục bắn khi
  đang copy dở).

### 1.7 Kiểm tra

Build Storage.Contracts, Services.Contracts, Services.Implementations, ba project Infrastructure. Script trong scratchpad
nhập một .osz mẫu và một thư mục mẫu: hash khớp, đủ file, tên file độc hại bị từ chối, entry zip mở lại được.

## 2. Spectating về `sessions.Spectators`

### 2.1 Hiện trạng

- Application mới không có service spectating riêng. `Basil.Application.Spectating` cũ (`SpectatorService`,
  `IPlayerInputEvents`, `SpectateFramesEvent`) bị gác lại từ `4c4b763a` và chưa đưa lại; chỉ các host chưa migrate còn
  tham chiếu.
- `IChannelService.Spectators` (`ChannelSpectatorService`) đang giữ luật spectate (quyền, tự xem mình, host offline,
  host tự vào kênh) và lưu quan hệ người xem bằng thành viên kênh `#spec`. Kênh mang nghiệp vụ không phải của kênh.
- `ChannelSpectatorService` quyết quyền theo loại kết nối (`by is TourneyConnection ? …`), điều AGENTS.md cấm.
- Relay frame spectate là relay tần số cao, không phải event (R9): vẫn ở ngoài Application, không thuộc kế hoạch này.

### 2.2 Thiết kế

Người bị tác động là osu! client của host, loại do session service quản lý; theo mẫu `RoomChannelService`, service
của host giữ quan hệ và giữ kênh `#spec` đồng bộ qua `IChannelService`.

- **Storage:** `BanchoConnection.Spectators` (`IReadOnlySet<Connection>`) và `internal ISet<Connection> MutableSpectators`
  (`ConcurrentSet`), cùng scope `internal Enter()` của connection cho chuyển trạng thái spectate. Bỏ
  `SpectatorChannelSession.Spectators`. `IUserRegistry.FindSpectating(connection)` trả về host (`BanchoConnection?`).
- **Contract:** `ISessionService.Spectators : ISessionSpectatorsService` với `Spectate(host, by)`, `StopSpectating(by)`,
  `ReportSpectatingFailed(by)`. `SpectateResult` chuyển từ Chat sang Sessions. Bỏ `IChannelSpectatorService`.
- **Luật:** mọi client cần `PlayerSpectate`. Kết nối IRC vẫn ném `ArgumentException`: đó là khả năng kỹ thuật, không phải
  quyền.
- **Kênh:** người xem đầu tiên kéo host vào `#spec`, người xem cuối rời thì host rời; vào/rời gọi
  `IChannelService` (sau mục 3 là `channels.Members.Join/Part`). `ChannelService` cho đọc/ghi kênh spectator khi là host
  hoặc nằm trong `host.Spectators`. Toàn bộ chuyển trạng thái (quan hệ và kênh) nằm trong một scope của connection host,
  thứ tự khóa luôn connection → kênh như phòng → kênh.
- **Event** (luồng `UserEvent` của session service):

  ```csharp
  public abstract record UserSpectatorEvent(BanchoConnection Host, Connection Spectator) : UserEvent;
  public sealed record UserSpectatorJoined(BanchoConnection Host, Connection Spectator) : UserSpectatorEvent(Host, Spectator);
  public sealed record UserSpectatorLeft(BanchoConnection Host, Connection Spectator) : UserSpectatorEvent(Host, Spectator);
  public sealed record UserSpectatorFailed(BanchoConnection Host, Connection Spectator) : UserSpectatorEvent(Host, Spectator);
  ```

  Bỏ `ChannelSpectatorJoined/Left/Failed` và cờ `HostJoined`/`HostLeft`: host vào/rời kênh do kênh báo bằng
  `ChannelMemberJoined`/`ChannelMemberParted` của chính nó.
- **Gọi tới:** `Infrastructure/Runtime/Sessions/ConnectionHandler.cs` (`StopSpectating` khi connection đóng), DI.

### 2.3 Quyền: `TournamentObserveRooms` chứa `PlayerSpectate`

```csharp
TournamentObserveRooms = 1UL << 16 | PlayerSpectate,
```

Hệ quả cần xử lý trong cùng commit:

- **Mặt nạ nhóm.** `Tournament` sẽ chứa bit của `PlayerSpectate`, nên `ToPrivileges()` bật `Tournament` cho mọi người có
  `PlayerSpectate`. Sửa: mỗi mặt nạ nhóm chỉ lấy bit của nhóm mình (`Tournament = (…) & ~Player`); kiểm lại
  `Management` và `All`.
- **Privilege hiển thị.** Ai giữ `TournamentObserveRooms` sẽ có thêm `ClientPrivileges.Player`. Hợp lý: họ xem được người
  chơi.
- **Restriction.** Treo `PlayerSpectate` làm `TournamentObserveRooms` không còn đủ bit, nên đăng nhập osu!tourney bị từ chối
  và kết nối tourney đang mở bị đóng. Đúng luật "phần nhỏ bị treo thì quyền lớn không còn trọn vẹn";
  `TournamentObserveRooms` không phải quyền quản lý nên không được giữ.
- **Dữ liệu đã lưu.** Tài khoản có bit 16 mà thiếu bit `PlayerSpectate` sẽ mất đăng nhập tourney. Kiểm seed/migration;
  nếu có dữ liệu thật thì cần một migration OR thêm bit.
- Kiểm bằng script: `PlayerSpectate` một mình không bật `ClientPrivileges.Tournament`; `TournamentObserveRooms`
  `Allows(PlayerSpectate)`.

## 3. Service chia theo phần con

### 3.1 Luật (ghi vào AGENTS.md, thay luật "A large service is split into child services by area")

> **A service whose object has parts gets one child service per part.** The parent contract exposes each child as a
> property (`IRoomService.Slots`); the object stays a parameter of every operation
> (`rooms.Slots.MoveAsync(room, by, player, index)`). Children share the parent's one event stream and may call a
> sibling's `internal` members; callers reach a child only through the parent. A service whose object has no parts it
> acts on stays flat.

### 3.2 Áp dụng

| Service           | Con (property → contract: thành viên)                                                                                                                                                                                                 | Ở lại cha                     |
|-------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-------------------------------|
| `IChannelService` | `Members` → `IChannelMembershipService`: `Join`, `Part`, `JoinAutoChannels`, `PartAll`; `Messages` → `IChannelMessagesService`: `PostAsync`; `General` → `IGeneralChannelService`: `Open`, `CreateAsync`, `ConfigureAsync`, `DeleteAsync` | `Close` (đóng mọi loại kênh)  |
| `ISessionService` | `Connections` → `ISessionConnectionsService`: `Close`, `MarkActive`, `CloseIdle`, `Revoke`, `ActFor`; `Presence` → `ISessionPresenceService`: `SetStatus`, `SetAway`, `SetPmPrivate`; `Spectators` → `ISessionSpectatorsService` (mục 2) | `Announce`                    |
| `IUserService`    | `Restrictions` → `IUserRestrictionsService`: `SilenceAsync`, `RestrictAsync`, `LiftAsync`                                                                                                                                              | `SetPermissionsAsync`, `DeleteAsync` |
| `ILobbyService`   | `Rooms` → `ILobbyRoomsService`: `OpenAsync`, `CloseAsync`; `Watchers` → `ILobbyWatchersService`: `Watch`, `Unwatch` (lobby giữ phòng và người xem, đúng luật; bỏ nếu thấy 2 + 2 là thừa)                                                  | —                             |

Để phẳng (đối tượng không có phần con được tác động riêng): `IAuthService`, `IBeatmapsetService`, `IScoreService`,
`IAnticheatService`, `IMatchService`.

### 3.3 Cài đặt

- Theo mẫu `RoomService`: cha là `internal sealed class XService(con…, XEventStream events) : IXService` chỉ chuyển
  property; mỗi con `internal sealed`, đăng ký trong `DependencyInjection`. `ChannelEventStream` đã có; sessions và users
  cùng phát `UserEvent` nhưng là hai luồng riêng, nên tách `SessionEventStream` và `UserEventStream`.
- File con nằm cạnh contract cha trong thư mục tính năng; `Multiplayer/Rooms/` giữ nguyên.
- Gọi tới phải sửa: `ChannelService`, `LobbyService`, `RoomChannelService`, `SessionService` (Services.Implementations);
  `ConnectionHandler`, `IdleConnectionSweeper`, `RuntimeStartup` (Infrastructure.Runtime). Host chưa migrate thì bỏ qua.
- XML doc: summary của con mô tả phần nó quản lý; doc thành viên chuyển nguyên.

## 4. Thứ tự, giao việc, kiểm tra

Ba phần chạm cùng Services.Contracts, Services.Implementations, `DependencyInjection.cs` và AGENTS.md, nên làm tuần tự,
không build song song cùng project:

1. **Beatmapset** (mục 1) → kiểm mục 1.7 → commit, push.
2. **Spectating và quyền** (mục 2): phải xong trước khi chia `IChannelService`, vì `Spectators` rời kênh trước → build
   Domain, Storage, Services, Infrastructure, script quyền → commit, push.
3. **Chia service** (mục 3) → build các project đã rework → commit, push.
4. **AGENTS.md**: luật 3.1; định nghĩa asset (thứ bên trong beatmap; archive là cả set, thuộc storage); spectating thuộc
   session và giữ kênh `#spec` đồng bộ; ví dụ quyền lồng khác nhóm (`TournamentObserveRooms` chứa `PlayerSpectate`) và
   luật mặt nạ nhóm → commit, push.

Giao việc: tự làm reader, storage, `BeatmapsetService`, spectating và quyền. OpenCode làm phần cơ học của mục 3 (tách
interface, chuyển thành viên, sửa chỗ gọi) bằng Rider MCP (`extract_interface`, `change_api_signature`, `rename_refactoring`),
prompt đủ chữ ký và doc, không để lựa chọn mở. Kiểm quota trước khi gửi. Đọc lại từng dòng theo mục "Domain and
Application" của AGENTS.md trước khi commit.

Kiểm cuối mỗi phần: `dotnet build` Domain, hai project Storage, hai project Services, ba project Infrastructure,
`src/Bot/Application`.
