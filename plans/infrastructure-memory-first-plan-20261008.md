# Kế hoạch: Infrastructure "bộ nhớ là sự thật", ghi DB từng lệnh, beatmapset lưu dạng giải nén

Trạng thái: **đã duyệt, đang triển khai** (sửa theo phản hồi lần 3 ngày 2026-10-08).

Không giữ tương thích ngược (chưa release phiên bản nào): thay đổi schema gộp thẳng vào `001_baseline.sql`, không viết
migration mới, không chuyển dữ liệu hay thư mục cũ.

Nguồn: hai lượt review Infrastructure ngày 2026-10-08 và phản hồi của chủ dự án. Đánh số 1–6 theo danh sách review
đầu: 1 banner, 2 try-op bị hủy, 3 match report, 4 `ConnectionHandler`, 5 `MatchRecorder`, 6 batch `Failed`. "Các mục
khác" gồm 7–11 của danh sách đầu và các phát hiện mới ở lượt review thứ hai.

## 0. Định nghĩa và nguyên tắc (đưa vào AGENTS.md ở pha cuối)

- **Bộ nhớ (memory) là nguồn sự thật.** Mọi thao tác dựa vào bộ nhớ trước. Chỉ khi bộ nhớ không có mới sang lớp tiếp
  theo là DB, để xác minh hoặc lấy dữ liệu về bộ nhớ. Thay đổi áp thẳng lên object trong bộ nhớ; DB được ghi song song
  làm bản chép để restart còn dữ liệu.
- **"Cache" chỉ có một nghĩa: thư mục `Cache/` trên đĩa**, chứa file dẫn xuất dựng lại được (`.osz` đóng gói lại, preview
  mp3). Không tên, XML doc hay tài liệu nào gọi phần dữ liệu trong RAM là cache. Đổi theo:
  - `CachedRepository` thành `MemoryRepository`;
  - bỏ `MatchReportCache`;
  - bỏ hẳn gói `Microsoft.Extensions.Caching.Memory` và mọi cơ chế TTL. Giữ object 5 phút sau lần dùng cuối cũng là
    một kiểu cache (TTL), nên bỏ: object theo nhu cầu sống đúng bằng vòng đời tham chiếu của nó (§3.1);
  - AGENTS: "a repository is a cache of live instances" và "derived read model … cached" được viết lại.
- **Hai loại dữ liệu:**
  - *Thường trực*: thứ mọi người dùng dù thế nào đi nữa. Nạp hết lúc khởi động, không bao giờ nhả; bộ nhớ là tập đầy
    đủ.
  - *Theo nhu cầu*: nạp khi cần, sống khi còn ai giữ (session giữ User, Room giữ Match, danh sách giữ phần tử, request
    đang chạy giữ object nó dùng) hoặc khi còn lệnh ghi chưa commit; hết thì được nhả. **Bộ nhớ không có nghĩa là chưa
    nạp, không phải là không tồn tại**: mọi lần tra trượt đều hỏi DB.
- **Quan hệ một-nhiều sống theo vòng đời của chủ.** Danh sách gắn với instance live của chủ: chủ còn thì danh sách
  còn, chủ được nhả thì danh sách đi theo. Nạp cả cụm lần đầu có người cần, sau đó chỉ thêm/sửa/xóa từng phần tử.
  - User → Restriction, Relationship;
  - Beatmapset → Beatmap;
  - Match → bản ghi trận (`IMatchRecord`: `Round`, `MatchEvent`);
  - Round → Score.
- **Ownership của Score là User** (`Root.User`). Match chỉ chứa Round; Round chứa danh sách Score.
- **Ghi DB từng lệnh một.** Chỉ gộp transaction khi một nghiệp vụ thật sự ghi nhiều dòng liên quan (hiện chưa có).
- **Lỗi:** chỉ thử lại lỗi do môi trường; mọi lỗi khác trả về hoặc ném cho bên gọi (§6.5).

## 1. Dữ liệu thường trực (mục 1)

**Lỗi gốc:** banner dùng cơ chế theo nhu cầu (`IdentityMap`, nhả sau 5 phút) nhưng lại coi bộ nhớ là đủ sau một lần
nạp. Hết 5 phút, `ListAsync` trả `[]` mãi mãi.

**Danh sách thường trực:**

| Dữ liệu | Hiện tại | Sau khi đổi |
|---|---|---|
| `ServerSettings` | Field, nạp lười | Nạp lúc khởi động |
| `GeneralChannel` | Field, nạp lười | Nạp lúc khởi động |
| `MenuBanner` | `IdentityMap` (lỗi) | `ConcurrentDictionary<Uri, MenuBanner>`, nạp lúc khởi động |
| Ảnh banner, icon menu, nền seasonal, FAQ | Đọc đĩa mỗi lần | Byte nằm trong bộ nhớ (`ConcurrentDictionary<string, byte[]>`), nạp lúc khởi động, ghi xuống đĩa song song; mở ra `MemoryStream` (seek được) |

- Mỗi repository/storage thường trực có `internal Task LoadAsync(CancellationToken)`. `StorageStartup` gọi sau migration
  và trước mọi service khác; lỗi nạp làm khởi động thất bại.
- Thao tác sau đó chỉ làm trên bộ nhớ và ghi bản chép (DB hoặc file).
- File thường trực không có thay đổi lúc đang chạy như Beatmaps (không watcher). Mọi thay đổi phải đi qua API; thư mục
  chỉ là bản chép để nạp lại lúc khởi động.

**Rà quy tắc "trượt nghĩa là chưa nạp" trên dữ liệu theo nhu cầu:** chỉ banner vi phạm.
- `RetainAsync` duyệt `Items.Values` chỉ để nhả những beatmap đang nạp; DB tự xóa phần còn lại. Đúng.
- `FindAsync` trả `null` khi khóa đang chờ xóa. Đúng, vì đó là thông tin có thật.

## 2. Try-op bị caller hủy (mục 2, đã đồng ý)

Token của caller chỉ có hiệu lực khi try-op **còn trong hàng, chưa bắt đầu**: gỡ khỏi hàng, không gì được ghi, caller
nhận `OperationCanceledException`. Đã bắt đầu thì bỏ qua token, chạy tới kết quả, cập nhật bộ nhớ. Chữ ký repository
giữ nguyên. Cài trong writer mới (§6).

## 3. `MemoryRepository`, quan hệ theo vòng đời, match report (mục 3)

### 3.1 `MemoryRepository`

- `Caching/CachedRepository.cs` → `Memory/MemoryRepository.cs`; namespace `…Storage.Caching` → `…Storage.Memory`.
  `IdentityMap` và `OwnedLists` đi theo. Đổi bằng Rider `rename_refactoring`/`move_type_to_namespace`.

```csharp
internal abstract class MemoryRepository<TKey, T>(Database database, DatabaseWriter writer)
	where TKey : notnull where T : class
{
	protected IdentityMap<TKey, T> Items { get; }
	protected Database Database { get; }
	protected DatabaseWriter Writer { get; }

	protected abstract TKey KeyOf(T item);
	protected abstract Root RootOf(T item);
	protected abstract Task<T?> LoadAsync(TKey key);           // một lần nạp phục vụ mọi caller, không nhận token
	protected abstract string WriteSql { get; }
	protected abstract object WriteParameters(T item);
	protected virtual WriteCommand EraseCommand(TKey key) => throw new NotSupportedException();
	protected virtual void CopyTo(T live, T from) { }           // khi caller đưa instance không live

	protected ValueTask<T?> FindAsync(TKey key, CancellationToken cancellationToken);  // bộ nhớ, trượt thì DB
	protected T Track(T item);
	protected Task SaveAsync(T item);    // gỡ tombstone của khóa, Track, CopyTo, writer.SaveAsync
	protected Task RemoveAsync(T item);  // nhả khỏi bộ nhớ, đặt tombstone tới khi lệnh xóa commit
}
```

- **`FindAsync`:** lần nạp chung chạy với `CancellationToken.None`; mỗi caller chờ bằng `.WaitAsync(token)` của riêng
  mình. Sửa lỗi "caller đầu hủy thì mọi caller bị hủy".
- **Tombstone:** `ConcurrentDictionary<TKey, Task> _removing`; continuation chỉ gỡ đúng task của nó. `SaveAsync` cùng
  khóa gỡ tombstone trước, nên "xóa rồi lưu lại" không còn tạo hai instance.
- **`CopyTo`:** mọi repo có `CreateOrUpdateAsync` chuyển phần chép field vào đây (User, Match, Round, Beatmapset,
  Restriction, UserStats, Beatmap). Restriction hiện không chép: sửa.
- **`IdentityMap`: không TTL, theo vòng đời tham chiếu.**
  - `ConcurrentDictionary<TKey, WeakReference<T>>`. Object sống khi còn ai giữ; weak reference chết thì entry được gỡ
    (lượt quét mỗi 1 024 lần thêm, như hiện tại).
  - **Ghim khi còn lệnh ghi chưa commit:** `Pin(key, item, Task committed)` giữ tham chiếu mạnh tới khi task xong.
    `MemoryRepository.SaveAsync`/`RemoveAsync` luôn ghim. Lý do: nếu object bị GC trong lúc lệnh ghi còn chờ, lần tra
    sau đọc DB (giá trị cũ) và bộ nhớ lệch với lệnh sắp commit.
  - Đánh đổi: object không ai giữ (ví dụ beatmap tra theo hash cho từng request) được đọc lại từ DB sau mỗi lần GC.
    Chấp nhận; đo nếu thấy chậm.
- **Chỉ mục phụ theo vòng đời object:** `ConcurrentDictionary<khóa phụ, WeakReference<T>>`, quét như trên; tra trúng
  vẫn kiểm lại khóa phụ trên object. Thay cho `_idsBySafeName`/`_safeNamesById` và `_idsByHash`/`_hashesById` (hiện
  không bao giờ thu nhỏ), và thêm chỉ mục checksum cho Score.

### 3.2 `OwnedLists` theo vòng đời chủ

- Danh sách giữ trong `ConditionalWeakTable<TOwner, Holder>`, khóa là **instance live của chủ**.
- Phần thay đổi chưa commit và vé nạp vẫn khóa theo id chủ, để chủ bị nhả rồi nạp lại vẫn áp đúng.
- Lần nạp chồng lên một thay đổi thì nạp lại (giữ nguyên logic hiện tại).
- Chữ ký: `OwnedLists<TOwnerKey, TOwner, T> where TOwner : class`, các method nhận `(TOwnerKey key, TOwner owner, …)`.

### 3.3 Bản ghi trận (`IMatchRecord`) theo vòng đời Match

`IMatchRecord` là interface Domain do `Round` và `MatchEvent` cài.

```csharp
internal sealed class MatchRecords(Database database, IUserRepository users)   // singleton, dùng chung
{
	// Bản ghi của trận theo Timestamp; chỉ nạp khi có người cần, sống cùng instance Match.
	public ValueTask<ImmutableList<IMatchRecord>> GetAsync(Match match, CancellationToken cancellationToken);
	// Thêm, hoặc thay Round cùng Number; chưa nạp thì chỉ giữ phần chưa commit, không nạp.
	public void Put(Match match, IMatchRecord record, Task committed);
}
```

- Bên dưới là `OwnedLists<int, Match, IMatchRecord>`. Chèn theo `Timestamp`, ổn định khi trùng.
- `IRoundRepository.ListAsync` lấy `OfType<Round>()`, `IMatchEventRepository.ListAsync` lấy `OfType<MatchEvent>()`.
  Contract không đổi.
- Vòng đời: Room giữ Match, Match giữ danh sách bản ghi, Round giữ danh sách Score. Phòng đóng và không ai còn giữ
  Match thì cả cụm được nhả.
- Đổi quy tắc AGENTS "append-only history (logins, match events) is not kept in memory": match event nằm trong bộ nhớ
  theo vòng đời trận. Login vẫn chỉ ghi DB.

### 3.4 Score thuộc User, Round chứa Score

- `PostgresScoreRepository`: `RootOf` và `CreateAsync` dùng `Root.User(UserId ?? 0)`.
- Danh sách Score theo Round **đặt ở repository**: `OwnedLists<(int MatchId, int Number), Round, Score>` trong
  `PostgresScoreRepository`. Nạp cả trận bằng một câu (`where match_id = @MatchId`) rồi chia theo round. Contract thêm:

```csharp
/// <summary>Lists the scores submitted in a round.</summary>
/// <returns>The round's scores, in submission order.</returns>
Task<IReadOnlyList<Score>> ListAsync(Round round, CancellationToken cancellationToken = default);
```

### 3.5 Match report chiếu từ bộ nhớ

- Xóa `MatchReportCache` và mọi chỗ invalidate.
- `MatchReportRepository.GetAsync`: lấy rounds của trận, rồi scores của từng round, rồi `RoundResult.Decide`. Tất cả
  đã ở bộ nhớ, không dựng lại từ DB.
- AGENTS: read model suy ra từ bộ nhớ khi đọc.

## 4. `ConnectionHandler` (mục 4)

Bốn bước (`ReleaseAsync`, `StopSpectating`, `Unwatch`, `PartAll`), mỗi bước một `try/catch`. Lỗi được log kèm tên
bước, và cả bốn bước luôn chạy. Thêm `ILogger<ConnectionHandler>`.

## 5. `MatchRecorder` (mục 5, đã đồng ý; Q2 đã chốt)

- Bỏ `BackgroundService`, channel 1024 và vòng thử lại; `HandleAsync` gọi thẳng repository. Bỏ đăng ký `IHostedService`.
- Thêm `public DateTimeOffset Timestamp { get; init; }` vào `RoomEvent` và `LobbyEvent`, đóng dấu tại chỗ phát duy nhất
  (`RoomEventStream.Emit`, `LobbyService.Emit`) bằng `@event with { Timestamp = time.GetUtcNow() }`. `MatchEvent` lấy
  `Timestamp` từ event.

## 6. Ghi DB từng lệnh (mục 6)

### 6.1 Bỏ gì, giữ gì

| Bỏ | Giữ |
|---|---|
| Transaction gom các root không liên quan | Mỗi root một hàng đợi, gửi tuần tự |
| Hẹn giờ 100 lệnh / 50 ms; 8 lane | Snapshot chưa gửi của cùng identity bị thay bằng bản mới nhất |
| Prelude và savepoint cho snapshot | Thử lại lỗi môi trường; xả hết hàng khi tắt server |

Lý do vẫn cần hàng đợi theo root: user 5 đổi country (A) rồi permissions (B). Nếu gửi ngay trên hai connection, B có thể
commit trước A, và DB kết thúc ở trạng thái cũ hơn bộ nhớ.

### 6.2 API

```csharp
internal sealed class DatabaseWriter : IHostedLifecycleService
{
	/// Snapshot: thay lệnh chưa gửi của cùng identity; task xong khi commit, lỗi nếu bị từ chối.
	public Task SaveAsync(Root root, object identity, WriteCommand command);
	/// Append: không bị thay; lệnh idempotent (insert … on conflict (id) do nothing).
	public Task AppendAsync(Root root, WriteCommand command);
	/// Try-op: chạy sau các lệnh trước của root, trong transaction riêng; token chỉ hủy khi chưa bắt đầu.
	public Task<T> WriteAsync<T>(Root root, Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> operation,
		CancellationToken cancellationToken = default);
}
```

- `ConcurrentDictionary<Root, RootQueue>`; mỗi `RootQueue` gồm `OrderedDictionary<object, Pending>` và cờ đang chạy.
  Có lệnh mà chưa chạy thì khởi một vòng xả; hàng rỗng thì tự gỡ (dưới lock của hàng).
- Giới hạn connection ghi đồng thời: `SemaphoreSlim(options.WriteConnections)`. Config `Basil:Database:WriteLanes`
  thành `Basil:Database:WriteConnections` (mặc định 8, đo lại ở §6.7).
- Không còn vòng nền: `StoppedAsync` chờ mọi hàng xả xong tới khi hết timeout shutdown; phần còn lại log critical.

### 6.3 Thực thi

- **Snapshot / append:** một lệnh autocommit, trên data source thứ hai có `Options=-c synchronous_commit=off`
  (`Database.OpenSnapshotConnectionAsync()`). Lỗi môi trường thì gửi lại; an toàn vì upsert và append đều idempotent.
- **Try-op:** id do DB cấp nên không idempotent, phải biết chắc đã commit hay chưa.
  - Trình tự: `begin; set local idle_in_transaction_session_timeout = '30s'; select pg_current_xact_id()`, lambda, `commit`.
  - Đứt kết nối lúc commit: hỏi `pg_xact_status(xid)`. "In progress" thì chờ; timeout 30 s bảo đảm server tự hủy, nên
    không treo nhiều giờ.
  - Gửi lại mù là sai: tạo user đã commit mà gửi lại sẽ đụng tên unique và báo "tên đã có" cho chính tài khoản đó.

### 6.4 Append có id do bộ nhớ cấp

`logins.id` và `match_events.id` lấy từ bộ đếm trong bộ nhớ, nạp lười một lần bằng `select coalesce(max(id), 0)`. Quy tắc
"một DB thuộc một tiến trình" bảo đảm không đụng id. Câu lệnh dùng `on conflict (id) do nothing`. Trong
`001_baseline.sql`, hai cột đổi thành `bigint primary key` (bỏ identity).

### 6.5 Lỗi: chỉ thử lại lỗi môi trường

| Loại | Gồm | Xử lý |
|---|---|---|
| Môi trường | `NpgsqlException.IsTransient` (I/O, socket, timeout); `PostgresException.IsTransient` (`53xxx`, `57P01`–`57P03`, `57P05`, `58xxx`, `08xxx`, `40001`, `40P01`, `55P03`, `55006`, `55000`); thêm `25006` (read-only sau failover), `57014` (bị hủy hoặc quá thời gian), `28000`/`28P01` (xác thực), `3D000` (thiếu database), `42501` (thiếu quyền) | Thử lại, backoff 50 ms → 5 s, log warning (error sau 1 phút liên tục) |
| Còn lại | Mọi lỗi khác, gồm lỗi bind parameter và mọi SqlState không có ở trên | **Không thử lại.** Try-op: ném cho bên gọi. Snapshot/append: task lỗi kèm exception và log error có identity. Không ai chờ snapshot, vì repository không chờ DB |

Batch nghiệp vụ (khi có) theo cùng quy tắc. Cần kiểm chứng khi làm: timeout lệnh ra `57014` hay
`NpgsqlException(TimeoutException)`; cả hai đều thuộc nhóm môi trường.

### 6.6 Transaction cho nghiệp vụ nhiều dòng

Chưa nghiệp vụ nào cần: import chạy lại được, đóng trận không cần nguyên tử, đăng ký có khe hở rất nhỏ. Không thêm API
lúc này. Khi cần thì thêm một try-op chứa các lệnh của đúng nghiệp vụ đó.

### 6.7 Đo lại

Chạy `bench.cs`/`verify.cs` (`plans/evidence/postgresql-write-lanes-20261008/`) cho cả 8 lane cũ và writer mới. Ghi
vào `plans/evidence/postgresql-direct-writes-<ngày>/`:
- steady 10 000 append/s cộng try-op p99;
- capacity 61 000 lệnh;
- exactly-once khi kill backend, thêm ca try-op mất kết nối lúc commit.

Không giữ nổi 10 000 append/s thì mới cân nhắc pipeline các lệnh liên tiếp của cùng một root.

## 7. Tạo bản ghi có khóa unique (Q3, đã chốt)

Hai trường hợp, áp cho mọi lần tạo hoặc đổi có khóa unique:
1. **Bộ nhớ đã có:** từ chối ngay, không gửi DB.
2. **Bộ nhớ chưa có:** gửi try-op. Ở DB, kiểm tra và tạo là **một** bước: chạy thẳng `insert` (không `select` trước);
   DB báo trùng bằng exception `23505` thì đó là tín hiệu "đã có". Từ chối, **không** nạp bản ghi đã có vào bộ nhớ.

**Từ chối bằng exception, không trả `null`.** Thêm vào `Basil.Application.Storage.Contracts.Common`:

```csharp
/// <summary>The exception thrown when a record cannot be created or changed because another record already has its
/// unique value.</summary>
public sealed class AlreadyExistsException(string message) : Exception(message);
```

| Thao tác | Khóa | Khi trùng |
|---|---|---|
| `IUserRepository.CreateAsync` | safe name | ném `AlreadyExistsException` |
| `IUserRepository.RenameAsync` | safe name | `false` (đổi tên, không phải tạo; giữ contract) |
| `IBeatmapsetRepository.CreateAsync` | online id | ném `AlreadyExistsException` |
| `IBeatmapRepository.CreateAsync` | hash, online id | ném `AlreadyExistsException` |
| `IBeatmapRepository.CreateOrUpdateAsync` (đổi hash) | hash | ném `AlreadyExistsException` (thay `InvalidOperationException`) |
| `IScoreRepository.CreateAsync` | checksum | ném `AlreadyExistsException` (thay `null`; `Task<Score>`) |

Bên gọi bắt exception:
- `AuthService.RegisterAsync`: trả lỗi tên đã có.
- `ScoreService`: xử lý như nộp trùng hiện tại.
- `BeatmapsetService.ImportAsync`: tạo set/beatmap bị trùng nghĩa là có import khác vừa tạo. Lấy bản đó bằng
  `GetAsync` rồi đi nhánh cập nhật. Không cần `SemaphoreSlim`.

Bước 1 cho user cần **luật safe name ở C#** (xem Q7).

## 8. Beatmapset lưu dạng giải nén; `.osz` nằm ở `Cache/`

### 8.1 Bố cục

```
Beatmaps/{setId}/…                     file của set, giải nén khi lưu (nguồn); tên thư mục như client
Cache/Beatmaps/{setId}/full.osz        .osz đầy đủ (có video nếu set có)
Cache/Beatmaps/{setId}/novideo.osz     chỉ khi set có video
Cache/Previews/{setId}-{UpdatedAt}.mp3 preview (Services)
```

### 8.2 Contract `IBeatmapsetStorage` (đổi)

```csharp
/// <summary>Stores the files of beatmapsets and offers each set as an .osz archive.</summary>
public interface IBeatmapsetStorage
{
	/// Stores a set's files from an .osz archive, replacing the files stored for it.
	/// Throws InvalidDataException when the archive is unsafe (path outside the set, too large).
	Task SaveAsync(Beatmapset set, Stream archive, CancellationToken cancellationToken = default);
	/// The relative names of the set's files; empty when nothing is stored for it.
	Task<IReadOnlyList<string>> ListAsync(Beatmapset set, CancellationToken cancellationToken = default);
	/// Opens one file of the set, matching the name without regard to case; null when absent.
	Task<Stream?> OpenAsync(Beatmapset set, string name, CancellationToken cancellationToken = default);
	/// Opens the set as an .osz, with or without its videos; null when nothing is stored for it.
	Task<Stream?> OpenArchiveAsync(Beatmapset set, bool withVideo, CancellationToken cancellationToken = default);
	/// Deletes the set's files and archives; deleting a missing set does nothing.
	Task DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default);
}
```

`BeatmapsetService.ScanAsync` đổi từ `OpenAsync(set) is null` sang `ListAsync(set)` rỗng.

### 8.3 Lưu (`SaveAsync`)

- Giải nén vào thư mục tạm cạnh `Beatmaps/`, rồi đổi chỗ với thư mục cũ. `DataPaths.Beatmapsets` đổi thành
  `DataPaths.Beatmaps` (thư mục `Beatmaps`).
- Mỗi entry đi qua `SafePath.Combine`. Từ chối archive vượt 2 000 entry hoặc 2 GiB giải nén
  (`// ponytail:` ghi giới hạn và cách nâng).
- Mtime của file đặt bằng `DateTime` của entry.
- Byte `.osz` gốc được lưu luôn làm `full.osz`, xóa `novideo.osz` cũ. Có video thì dựng `novideo.osz` (§8.5).

### 8.4 Đọc

- `OpenAsync(set, name)` trả `FileStream` (`FileShare.Read | FileShare.Delete`). Seek được nên host phục vụ được Range.
- `BeatmapAssets` (Services) không giải nén gì nữa:
  - `.osu` theo MD5: liệt kê các file `.osu` của set và băm từng file (`// ponytail:` băm mỗi lần; cần thì giữ chỉ mục
    hash → tên theo vòng đời set);
  - background, audio, video, storyboard: mở theo tên lấy từ `.osu`;
  - `Archive` và `ArchiveWithoutVideo`: gọi `OpenArchiveAsync`.
- Preview mp3 dựng một lần, ghi `Cache/previews/`. Ffmpeg đọc audio qua pipe (`StreamPipeSource`); nếu seek trên pipe
  không chạy thì chép audio ra file tạm, xong xóa ngay.

### 8.5 Cập nhật delta theo thông báo của file system (SharpZipLib)

- `BeatmapsetFilesWatcher : BackgroundService` (Storage) theo dõi `Beatmaps/` kèm thư mục con, gom sự kiện theo
  `setId`, debounce 2 s. Khi tới hạn, gọi `FileBeatmapsetStorage.SyncArchivesAsync(setId)`.
- `SyncArchivesAsync` so thư mục với từng archive đang có (tên, kích thước, mtime ±2 s vì giờ DOS trong zip):
  - mở archive bằng `ZipFile`, `BeginUpdate()`;
  - `Add` file mới hoặc đã đổi, `Delete` entry không còn file;
  - `CommitUpdate()`. SharpZipLib chép thẳng dữ liệu đã nén của entry không đổi (`CopyEntryDirect`), chỉ nén file đổi.
  - File video chỉ đi vào `full.osz`. Set mới có video thì dựng `novideo.osz`; set hết video thì xóa nó.
- Archive chưa có thì bỏ qua; `OpenArchiveAsync` dựng đầy đủ lần đầu bằng `ZipOutputStream`.
- Lúc khởi động, `SyncArchivesAsync` chạy nền cho mọi set, để bắt thay đổi lúc server tắt (so sánh không phụ thuộc sự
  kiện bị sót). Sự kiện tràn bộ đệm (`Error`) cũng kích hoạt đồng bộ toàn bộ.
- Mỗi set một `SemaphoreSlim` (giữ trong dictionary, không gỡ sau khi release) cho dựng và đồng bộ.
- Spike khi làm: xác nhận `CommitUpdate` không nén lại entry không đổi, và người đang đọc archive (`FileShare.Delete`)
  không chặn việc thay file trên Windows.
- **Không sửa `.osu` trong `novideo.osz`.** Client nhận diện difficulty bằng MD5 nội dung `.osu`; osu-web cũng chỉ bỏ
  file video, và client bỏ qua video thiếu (DeepWiki `ppy/osu-web`, `ppy/osu`).
- Gói: `SharpZipLib` vào `Directory.Packages.props` và Storage. Services không cần (reader import vẫn dùng
  `System.IO.Compression`).

### 8.6 Dọn và giới hạn

- **Dọn preview:** thêm vào `IBeatmapAssets`:

```csharp
/// <summary>Discards the files derived from a beatmapset, such as its audio preview.</summary>
Task ForgetAsync(Beatmapset set, CancellationToken cancellationToken = default);
```

  Handler `Runtime/Beatmaps/BeatmapAssetsHandler : IEventHandler<BeatmapsetEvent>` gọi nó cho `BeatmapsetDeleted` và
  `BeatmapsetImported`. Archive của set do `IBeatmapsetStorage.DeleteAsync` xóa.
- **Zip bomb ở reader:** `OsuBeatmapsetReader` bỏ qua entry `.osu` lớn hơn 16 MiB và chỉ đọc tối đa ngần ấy byte.

## 9. Các mục khác (đã kiểm và phân loại)

**Sửa thẳng khi kế hoạch được duyệt, không hỏi lại:**

| Mục | Phân loại | Sửa |
|---|---|---|
| `IdleConnectionSweeper` không bắt lỗi; host mặc định `StopHost` | Lỗi thật | `try/catch` mỗi tick, log error |
| `BeatmapImportWatcher` setup lỗi làm sập host | Lỗi thật | Bắt lỗi setup, log error |
| Phân trang beatmap không ổn định | Lỗi thật | Thêm `, b.id` vào `order by` |
| `%`/`_` thành wildcard; dấu cách thành `_` khớp mọi ký tự | Lỗi thật | Escape `\ % _`; dấu cách thành `\_`; áp cho user search và `BeatmapQueryFilter` |
| Icon menu xóa cũ trước khi ghi mới | Lỗi thật | Ghi mới xong mới xóa phần còn lại (nay qua bộ nhớ, §1) |
| Lần nạp chung dùng token của caller đầu | Lỗi thật | §3.1 |
| Chỉ mục tên/hash không bao giờ thu nhỏ | Rò rỉ chậm | §3.1 (chỉ mục phụ theo vòng đời) |
| `DataPaths.Imports` trùng `BeatmapImportOptions` | Dư thừa | Bỏ `Imports` khỏi `DataPaths`; watcher import tự tạo; host suy option từ data directory. `DataPaths.Cache` giữ lại (nay Storage dùng, §8) |
| Import chặn khởi động; file thả vào lúc chuyển giao bị sót | Lỗi thật nhỏ | Watcher bật `FileSystemWatcher` trước, rồi liệt kê file sẵn có trong nền; `ScanAsync` chạy nền; `RuntimeStartup` giữ mở kênh và `CloseUnfinishedAsync` |
| Restriction không chép field | Không nhất quán | §3.1 (`CopyTo`) |

**Thiết kế mới tự giải quyết:**
- lane treo vì "in progress" (§6.3);
- cả batch mất vì một lệnh, `Rejected` bỏ dữ liệu vì lỗi môi trường (§6.5);
- `MatchRecorder` không xả khi tắt (§5);
- thứ tự đăng ký host do lane chưa chạy (writer không còn vòng nền; chỉ còn ràng buộc migrate trước `RuntimeStartup`,
  ghi vào XML doc của `AddInfrastructureRuntime`);
- `PostgresException` lọt lên Application khi tạo trùng (§7);
- lock thư mục và trùng tên trong `BeatmapAssets` (§8).

## 10. Quyết định

**Đã chốt:**
- Q1: danh sách Score theo Round đặt ở repository.
- Q2: `Timestamp` trên `RoomEvent`/`LobbyEvent`.
- Q3: tạo trùng (§7).
- Q4: viết lại câu "no database" trong AGENTS: tạo identity và kiểm tra unique cần DB; DB sập thì các thao tác đó chờ,
  phần còn lại chạy.
- Q5: dữ liệu thường trực (§1).

**Q6, Q7 duyệt theo mặc định:**

| # | Câu hỏi | Mặc định |
|---|---|---|
| Q6 | Sửa tay file `.osu` trong `Beatmaps/{id}/` (đổi MD5, beatmap trong bộ nhớ lệch) | Chỉ import mới đổi `.osu`. Watcher vẫn đồng bộ archive và log warning "import lại để cập nhật beatmap". Storage không phát event (AGENTS), nên tự đọc lại cần watcher ở Runtime và method mới ở `IBeatmapsetService`; để sau |
| Q7 | Luật safe name (cần cho bước 1 của §7) | Chuyển về Domain: `UserData.SafeName` = `ToLowerInvariant()` rồi dấu cách thành `_`. Sửa thẳng `001_baseline.sql`: `safe_name text not null unique` (cột thường); app ghi `safe_name` khi tạo và đổi tên. Đảo câu AGENTS "rules the database owns for its lookups (the safe name) stay in the database" |

## 11. Thi công (sau khi duyệt)

Theo `~/.claude/CLAUDE.md`:
- Ngay trước khi giao việc: kiểm tra quota OpenCode (trang Go, `opencode stats --days 0/7 --models --cost`) và chạy thử
  một lần để xác nhận đúng repo.
- Mình viết spec đầy đủ cho từng gói, không để lựa chọn mở; OpenCode chỉ cài.
- Agent dùng Rider MCP (`rename_refactoring`, `move_type_to_namespace`, `get_file_problems`), CodeGraph và các skill .NET.
- Việc cơ học chạy trên model rẻ (`mimo-v2.6-flash`, `deepseek-v4.1-flash`); writer, `MemoryRepository`, đồng bộ
  archive chạy trên `qwen3.7-plus` hoặc `gpt-6-luna`.
- Mình review từng dòng theo AGENTS ("Final verification" mục 7), commit rồi push.

| Pha | Nội dung | File chính | Song song? |
|---|---|---|---|
| P1 | §9 sửa thẳng, §4 | `Runtime/Sessions/*`, `Runtime/Beatmaps/*`, `Storage/Beatmaps/BeatmapQueryFilter.cs`, `Storage/Users/PostgresUserRepository.cs` (escape), `Storage/DataPaths.cs` | Cùng P5 |
| P2 | §6, §2 | `Storage/Writing/*`, `Storage/Database.cs`, `Storage/DatabaseOptions.cs`, `Users/PostgresLoginRepository.cs`, `Multiplayer/PostgresMatchEventRepository.cs`, `Storage/Migrations/001_baseline.sql` | Sau P1 |
| P3 | §3, §1, §7, Q7 | `Storage/Memory/*`, mọi repository, `Storage/StorageStartup.cs`, `Storage/Migrations/001_baseline.sql`, `Storage/Files/File{MenuBanner,MenuIcon,MenuSeasonals,Faq}Storage.cs`, contracts `IScoreRepository`/`IUserRepository`/`IBeatmapRepository`/`IBeatmapsetRepository`, `Domain/Users/UserData.cs`, `AuthService.cs`, `BeatmapsetService.cs` | Sau P2 |
| P4 | §5 | `Runtime/Multiplayer/MatchRecorder.cs`, `Runtime/DependencyInjection.cs`, `Services.Contracts/Multiplayer/*Events.cs`, `RoomEventStream.cs`, `LobbyService.cs` | Sau P3 |
| P5 | §8 | `Storage/Files/FileBeatmapsetStorage.cs`, `Storage/Files/BeatmapsetFilesWatcher.cs`, `Storage.Contracts/Beatmaps/IBeatmapsetStorage.cs`, `Services/Beatmaps/*`, `Services.Contracts/Beatmaps/IBeatmapAssets.cs`, `Runtime/Beatmaps/BeatmapAssetsHandler.cs`, `BeatmapsetService.ScanAsync`, `Directory.Packages.props` | Cùng P1 (P3 cũng sửa `BeatmapsetService`: P5 làm trước, P3 rebase) |
| P6 | Đo lại §6.7; AGENTS (§0, Q4, Q7, lane/batch/`WriteLanes`/link evidence, "append-only history", "derived read model", "cache of live instances") | `plans/evidence/…`, `AGENTS.md` | Cuối |

Kiểm chứng sau mỗi pha (không chạy hai build cùng lúc):

```
dotnet build src/Infrastructure/Storage/Basil.Infrastructure.Storage.csproj
dotnet build src/Infrastructure/Services/Basil.Infrastructure.Services.csproj
dotnet build src/Infrastructure/Runtime/Basil.Infrastructure.Runtime.csproj
dotnet build src/Application/Services/Implementations/Basil.Application.Services.Implementations.csproj
dotnet build src/Application/Storage/Implementations/Basil.Application.Storage.Implementations.csproj
dotnet build src/Bot/Application/Basil.Bot.Application.csproj
```

`tests/` và host chưa migrate (AGENTS), nên chưa chạy `dotnet test`. Hành vi ghi DB kiểm bằng `verify.cs`/`bench.cs`
(§6.7) trên PostgreSQL local; đồng bộ archive kiểm bằng một script spike trong `plans/evidence/` (sửa, thêm, xóa file
rồi mở `.osz` bằng `ZipArchive` so nội dung).

## Trạng thái thi công (cập nhật 2026-10-08)

Đã commit: P1 `1a5e1119`, P2 `8c05f2b7`, P3a `456be753`, P3b A+C `f58b2ced`, P3b B `db399543`, P4 `0d92ed81`, AGENTS
(đoạn "Memory is the truth"). Lệch so với plan: bản ghi trận là hai danh sách theo vòng đời Match (rounds, events)
thay vì một `MatchRecords` chung; preview mp3 chép audio ra file tạm rồi chạy ffmpeg (không dùng pipe).
Còn lại: P5 (spec `.tasks/p5.md`, đang chạy trên OpenCode lúc ghi), P6 đo lại `bench.cs`/`verify.cs` với writer mới
(PostgreSQL 18 local đang chạy; đổi reflection `EnqueueAsync` → `SaveAsync`, `WriteLanes` → `WriteConnections`).
