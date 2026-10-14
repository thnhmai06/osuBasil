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

## 5. Bổ sung 2026-10-14: kho beatmapset lấy archive.osz làm nguồn thật

Phần này thay cách lưu "một folder chứa file rời" của mục 1 và phần đang làm dở theo hướng alpha (folder
`{id} {Artist} - {Title}`, quét và theo dõi thư mục gốc). Chưa đụng vào code, chờ duyệt.

### 5.1 Đã chốt (phỏng vấn 2026-10-14)

| Câu hỏi               | Chốt                                                                                                     |
|-----------------------|----------------------------------------------------------------------------------------------------------|
| Thư mục gốc           | `Data/Songs` đổi thành `Data/Beatmapsets`; `Data/Songs`, `Data/Beatmaps` cũ chuyển sang lúc khởi động     |
| Tên folder set        | Giữ: `{id} {Artist} - {Title}`                                                                           |
| Đầu vào               | Thả vào gốc `Data/Beatmapsets` hoặc upload qua API. `.osz` chuyển nguyên byte thành `archive.osz`; folder file rời (kiểu osu! Songs) được nén thành `archive.osz`, file rời bị thay bằng cấu trúc chuẩn |
| Nguồn thật            | Beatmapset: `archive.osz`. Beatmap (chỉ để nạp tính độ khó): `versions/{md5}.osu`                        |
| File dựng ra bị thiếu | Dựng lại từ `archive.osz`; set chỉ bị loại khi `archive.osz` mất hoặc không đọc được                     |
| Hash khi quét         | Đường tắt: DB lưu kích thước và thời gian sửa của `archive.osz`; khởi động chỉ hash khi một trong hai khác. Sự kiện watcher thì luôn hash |
| Độ khó theo mods      | Giữ một object đại diện độ khó thô của beatmap, do thư viện osu! chính thức tạo ở Infrastructure; Application chỉ thấy nó là một object |

### 5.2 Cấu trúc một folder set

```text
Data/Beatmapsets/
  {id} {Artist} - {Title}/
    archive.osz            nguồn thật: nguyên bản, có video
    archive-novideo.osz    dựng từ archive.osz, bỏ video
    versions/{md5}.osu     mỗi difficulty, tên theo hash nội dung (= beatmaps.hash)
    backgrounds/{tên}      từng ảnh nền; difficulty nào dùng ảnh nào thì DB lưu
    audio/{tên}            từng file nhạc; DB lưu difficulty nào dùng file nào
    audio/preview_{tên}    đoạn nghe thử dựng từ audio/{tên} (mẫu cố định, {tên} lấy từ DB)
    video/{tên}            từng video; DB lưu difficulty nào dùng video nào
    storyboard.osb         storyboard của set, nếu có
```

- Không còn thư mục `assets/`. Không giải nén thứ nghiệp vụ không dùng (sprite storyboard, hitsound, skin...):
  chúng vẫn nằm trong `archive.osz`, nên tải về vẫn đủ.
- Hai file `.osz` không còn ở `Cache/`; `Cache/Beatmaps` và preview trong `Cache/` bị bỏ.
- Tên file lấy theo tên gốc (chỉ phần tên, đã lọc ký tự không hợp lệ). Hai file khác nhau trùng tên trong cùng
  loại thì file sau thêm hậu tố `-2`, `-3`; DB lưu tên thật.

### 5.3 Dữ liệu trong DB

- `beatmapsets`: thêm `archive_hash` (md5 của `archive.osz`, khoá nhận diện nội dung), `archive_size` và
  `archive_modified_at` (đường tắt khi quét), `storyboard` (có hay không).
- `beatmaps`: `hash` (đã có) là tên file trong `versions/`; thêm `background`, `audio`, `video` (tên trong thư mục
  tương ứng, có thể null) và `preview_time`. Tên preview suy ra: `preview_{audio}`.
- Domain: `BeatmapsetData` thêm `ArchiveHash` (`Md5`), `ArchiveSize`, `ArchiveModifiedAt`, `Storyboard`; `BeatmapData`
  thêm `Background`, `Audio`, `Video`, `PreviewTime`. Migration PostgreSQL mới.
- Giữ nguyên metadata đang có trong DB, kể cả `beatmaps.objects` (jsonb, số object theo mode) và độ khó NoMod.

### 5.4 Quét và theo dõi

"Folder" ở đây là mọi folder con độ sâu 1 của `Data/Beatmapsets/`, hợp lệ hay không.

**Quét toàn bộ (lúc khởi động).** Phải xong trước khi các host khởi động:

1. **Kiểm tra.** Đi qua mọi entry ở gốc và đưa vào hàng đợi những gì cần index lại:
   - `.osz` rời: luôn vào hàng đợi;
   - folder không có `archive.osz` nhưng có file rời đọc được: vào hàng đợi (sẽ được nén);
   - folder có `archive.osz`: so kích thước và thời gian sửa với DB trước; khớp thì không hash. Không khớp thì
     hash; hash khác `beatmapsets.archive_hash`, hoặc DB chưa có set, thì vào hàng đợi. Hash khớp thì chỉ ghi nhận kích
     thước và thời gian mới;
   - set có trong DB mà không còn hợp lệ trên đĩa (mất folder hoặc mất `archive.osz`): đánh dấu bỏ khỏi hệ thống.
2. **Index lại** từng mục trong hàng đợi, đồng thời đồng bộ file dựng ra:
   - `.osz` rời: đọc metadata, chọn set (trùng hash difficulty, rồi id file khai, rồi set mới), chuyển nguyên byte
     vào `{folder}/archive.osz`;
   - folder file rời: nén thành `archive.osz` trong folder chuẩn của set, xoá file rời;
   - rồi phân tích, dựng lại mọi file của mục 5.2 (kể cả preview) và đổi tên folder về tên chuẩn;
   - `archive.osz` không đọc được: set không hợp lệ, đánh dấu bỏ khỏi hệ thống, giữ nguyên folder.
   - Set không cần index lại nhưng thiếu file dựng ra: dựng lại từ `archive.osz`.
3. **Ghi DB một lần.** Xong hết thì gửi mọi thay đổi (set và beatmap thêm, sửa, bỏ; kích thước và thời gian mới)
   trong một transaction. Đây là try-operation riêng của việc quét, đúng luật "một thao tác nghiệp vụ cần lưu
   nhiều dòng cùng nhau thì có try-operation riêng". Bộ nhớ cập nhật theo kết quả sau khi transaction xong; event
   (`BeatmapsetImported`, `BeatmapsetDeleted`) phát sau đó.

Khởi động: Runtime đăng ký một `IHostedService` chạy quét toàn bộ trong `StartAsync`; host đăng ký Infrastructure
trước web server, nên server chỉ nhận kết nối khi quét xong. Watcher bắt đầu ghi nhận thay đổi trước khi quét, và
xử lý chúng sau khi quét xong.

**Theo dõi trực tiếp.** Watcher báo từng entry thay đổi (debounce 2 giây):

- `archive.osz` của bất kỳ folder nào thay đổi: index lại ngay, kể cả folder trước đó không phải beatmapset hợp lệ.
- `.osz` rời hoặc folder file rời mới: index như trên.
- Folder hay `archive.osz` biến mất: bỏ set khỏi hệ thống.
- Mỗi lần index một entry là một thao tác riêng: ghi DB bằng try-operation của nó rồi phát event.

Quét lại một entry không đổi thì không ghi DB, không ghi file, không phát event. Xoá set: đổi tên folder có
`.deleted_` rồi xoá ngay; khởi động dọn phần còn sót. Staging vẫn ở `Cache/Staging`, cùng ổ với
`Data/Beatmapsets`.

### 5.5 Nội dung .osu đã đọc hiểu (độ khó theo mods)

- Domain: lớp trừu tượng rỗng `BeatmapContent` (không thành viên nghiệp vụ), đại diện nội dung đã đọc hiểu của
  toàn bộ file `.osu`: các object thật (hit object, timing, thiết lập độ khó...). Infrastructure.Services cài đặt
  bằng `IWorkingBeatmap` của thư viện osu! (lớp `StreamlessWorkingBeatmap` đang có, bọc `Beatmap` đã giải mã; audio,
  texture, skin trả null), vì đó là kiểu tối thiểu mà `CreateDifficultyCalculator` và `GetPlayableBeatmap` nhận.
  Mỗi lần tính theo mods chỉ tạo calculator và chuyển đổi lại, không đọc hay giải mã file. Nó không thay metadata
  trong DB.
- `Beatmap` có thuộc tính `Content`, khởi tạo lười: rỗng khi tạo object, chỉ được nạp lần đầu có người cần.
  Setter chỉ gán.
- `IBeatmapAnalyser`:
  - `Task<BeatmapAnalysis> AnalyzeAsync(Beatmap beatmap, GameMods mods)`: nếu `Content` chưa có thì nạp từ
    `versions/{md5}.osu` (qua `IBeatmapsetStorage`) rồi gán vào beatmap; sau đó tính theo mods từ `Content`, không
    đọc lại file;
  - `BeatmapAnalysis Analyze(Stream osu, GameMode mode, GameMods mods)` vẫn dùng lúc index.
- `Content` sống trong bộ nhớ cùng beatmap (on-demand data), không xuống DB.

### 5.6 Contract thay đổi so với phần đang làm

- `IBeatmapsetStorage` làm việc với folder set theo cấu trúc mục 5.2:
  - `SaveAsync(set, Stream archive)` ghi `archive.osz` nguyên byte; nén folder file rời thành `archive.osz`;
  - dựng file từ archive: `archive-novideo.osz`, `versions/`, `backgrounds/`, `audio/`, `video/`, `storyboard.osb`;
  - `OpenAsync(set, kind, name)` mở một file dựng ra; `OpenArchiveAsync(set, withVideo)` thay `ExportAsync`. Thiếu
    file thì trả "không có", không dựng;
  - `ListAsync()`, `WatchAsync()`, `DeleteAsync(...)` giữ như phần đang làm. Entry là `.osz` rời hoặc một folder
    độ sâu 1 (có hay không có `archive.osz`, kèm kích thước và thời gian sửa).
  - Host đọc file dựng ra thẳng từ storage (outer layers use storage directly).
- `IBeatmapsetReader`: chỉ còn đọc archive; overload đọc danh sách file bị bỏ.
- `IBeatmapAssets` chỉ còn dựng file phái sinh lúc index, ví dụ preview bằng FFmpeg, ghi vào `audio/preview_{tên}`.
  Không còn mở file và không dựng khi được hỏi.
- Repository: thêm try-operation ghi kết quả một lần quét (set, beatmap thêm, sửa, bỏ) trong một transaction.
- `IBeatmapsetService.ScanAsync()` / `ScanAsync(entry)` theo mục 5.4; `ImportAsync(Stream)` (upload API) đi đúng
  luồng `.osz` rời.
- Giữ từ phần đang làm (chưa commit): tên folder và tìm theo id đứng đầu, marker `.deleted_`, staging ngoài thư mục
  gốc, `WatchAsync` có debounce, `BeatmapsetStorageWatcher` ở Runtime (đổi thành chạy quét trong `StartAsync`),
  dọn rác lúc khởi động. `AdoptAsync` và `BeatmapsetFolderEntry.ListFiles` bị thay.

### 5.7 Thứ tự và kiểm tra

1. Domain (`BeatmapContent`, cột mới) và migration; contracts mục 5.5 và 5.6. Build các project contract.
2. Song song, mỗi project một agent OpenCode, mình review từng dòng:
   - Infrastructure.Storage: cấu trúc folder, nén folder rời, dựng file, quét, xoá, chuyển thư mục cũ,
     try-operation ghi kết quả quét;
   - Infrastructure.Services: reader, analyser (`AnalyzeAsync` với `Content` lười), dựng preview lúc index;
   - Services.Implementations: `BeatmapsetService` theo mục 5.4;
   - Runtime: quét trong `StartAsync`, rồi theo dõi.
3. Script kiểm trên `.osz` mẫu:
   - thả `.osz` → đúng cấu trúc, `.osz` gốc biến mất, preview đã có sẵn;
   - thả folder rời → được nén;
   - quét lần hai → không ghi file, không ghi DB, không event;
   - xoá `versions/x.osu` → dựng lại;
   - sửa `archive.osz` → index lại, kể cả folder trước đó hỏng;
   - xoá `archive.osz` → set bị bỏ, folder giữ nguyên;
   - `AnalyzeAsync` với HR/DT ra khác NoMod, và lần gọi thứ hai không mở file;
   - kiểm DI.
4. AGENTS.md: luật lưu beatmapset và luật `BeatmapContent`. Commit, push.

### 5.8 Đã chốt (2026-10-14, lượt 2)

1. Set lưu `archive_hash`, `archive_size`, `archive_modified_at`.
2. Quét toàn bộ lúc khởi động theo hai bước (kiểm tra rồi index lại), ghi DB một transaction, xong mới khởi động
   host.
3. Nội dung `.osu` đã đọc hiểu (`BeatmapContent`) là thuộc tính trên beatmap, khởi tạo lười; metadata vẫn ở DB.
4. Mọi file phái sinh dựng sẵn lúc index; khi được hỏi chỉ mở, mất thì trả "không có".
5. Set có `archive.osz` hỏng: giữ folder.
6. `archive.osz` của mọi folder độ sâu 1 thay đổi trực tiếp thì index lại, kể cả folder không hợp lệ.

7. Giữ `beatmaps.objects` và mọi metadata đang có.
## 6. Bổ sung 2026-10-14: spectating và trạng thái thuộc osu! client

Session là tập hợp các connection; spectating và trạng thái là nghiệp vụ chỉ osu! client có (`BanchoConnection`).
Người gọi tìm đúng connection rồi mới làm: tìm ai, rồi làm gì.

- `ISessionConnectionsService` có thêm phần con `Bancho` (`IBanchoConnectionService`), thao tác chỉ osu! client có:
  - `SetStatus(BanchoConnection by, PlayerStatus status)`, chuyển từ `Presence` sang;
  - `Spectators` (`IBanchoSpectatorsService`): `Spectate(host, by)`, `StopSpectating(by)`,
    `ReportSpectatingFailed(by)`, chuyển từ `ISessionSpectatorsService` (bỏ contract này).
- `ISessionService` còn `Connections`, `Presence` (`SetAway`, `SetPmPrivate`: thứ user chia sẻ trên mọi connection)
  và `Announce`; bỏ `Spectators`.
- Gọi: `sessions.Connections.Bancho.Spectators.Spectate(host, by)`, `sessions.Connections.Bancho.SetStatus(by, status)`.
- Quan hệ vẫn ở `BanchoConnection.Spectators`; event giữ nguyên (`UserSpectator*`, `UserConnectionStatusChanged`) trên
  luồng của session service.
- Đối chiếu bancho.py, sửa luôn trong đợt này:
  - spectate người khác khi đang xem một host: rời host cũ trước (phát `UserSpectatorLeft` cho host cũ);
  - spectate lại đúng host đang xem: phát lại `UserSpectatorJoined` (client gửi lại sau khi tải map xong), trả
    `Spectating`; bỏ `SpectateResult.AlreadySpectating`.
- Screenshot (đối chiếu bancho.py): `IScreenshotService.UploadAsync(by, content)` không nhận phần mở rộng từ caller;
  service đọc tối đa 4 MiB, nhận diện PNG/JPEG theo đầu và cuối file, trả lý do khi từ chối (`NotAuthorized`,
  `TooLarge`, `InvalidImage`).
- AGENTS.md: sửa ví dụ phần con của `ISessionService` và câu "Spectating is a relation ...".

Thứ tự: làm cùng bước 1 của mục 5 (contracts), rồi cài đặt trong Services.Implementations và Runtime
(`ConnectionHandler`).
