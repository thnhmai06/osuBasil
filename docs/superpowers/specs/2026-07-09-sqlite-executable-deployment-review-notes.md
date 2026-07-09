# SQLite + Executable Deployment — Ghi chú quyết định (cần review)

Viết trong lúc triển khai autonomous (không hỏi lại giữa chừng theo yêu cầu). Liệt kê các quyết định
không hiển nhiên mà tôi tự đưa ra — review lại, có gì sai/không đồng ý thì nói để sửa.

## 1. `Database:Path=""` làm sentinel skip-migration cho test host

`DatabaseOptions.Path` giờ có default `"basil.db"` (không rỗng) để deployment thật chạy zero-config.
Nhưng test host (`WebApplicationFactory`) cần 1 cách để nói "không có DB thật, đừng migrate/query" —
trước đây (MySQL) việc này "tình cờ" hoạt động vì `Host` là required nhưng binding runtime không
enforce, nên để trống config → `Host` rỗng → guard cũ skip. Với SQLite, `Path` luôn có default nên
guard cũ không còn phân biệt được "test" vs "deployment thật dùng default".

**Quyết định:** 9 file test trong `Basil.IntegrationTests` set tường minh
`["Database:Path"] = ""` trong config test. `Program.cs` dùng `string.IsNullOrEmpty(dbOptions.Path)`
làm guard — skip migration + `IChannelRepository.FetchAllAutoJoinAsync` + beatmap ingestion + bot
bootstrap khi rỗng, nhưng vẫn luôn gọi `channelRegistry.Seed(...)` (với list rỗng khi không có DB) để
registry không bao giờ ở trạng thái chưa-seed.

**Rủi ro nếu sai:** nếu sau này có thêm 1 IntegrationTests file mới không set `Database:Path=""` và
không stub các repository nó chạm tới, request sẽ 500 vì SQLite connection string rỗng
(`Data Source=;...`) không mở được. Không có compile-time nào bắt lỗi này — chỉ test runtime mới lộ.

## 2. Dapper không materialize trực tiếp `record` — phải qua DTO trung gian

3 chỗ (`MatchRow`, `RoundRow` trong `SqliteMatchPersistenceRepository`, `FirstPlaceScoreRow` trong
`SqliteScoreRepository`) từng query thẳng vào public `record` (constructor vị trí). Dưới MySQL việc
này hoạt động vì MySqlConnector trả về đúng kiểu `Int32`/`DateTime`. Microsoft.Data.Sqlite trả về
`Int64` cho mọi cột INTEGER affinity và `string` cho cột lưu ngày-giờ dạng TEXT — Dapper's fast-path
cho record đòi khớp kiểu chính xác với constructor, nên throw `InvalidOperationException` lúc chạy
(bị 7 test trong `Basil.Infrastructure.Tests` bắt được).

**Quyết định:** thêm private mutable DTO class (`MatchRowDto`, `RoundRowDto`,
`FirstPlaceScoreRowDto`) map lỏng theo tên cột rồi `.ToRow()` sang record công khai — đúng pattern đã
có sẵn ở hầu hết repo khác (`UserRow`, `MapRow`, v.v.), chỉ 3 repo này trước đó query thẳng record nên
mới lộ ra.

**Chưa kiểm tra:** không có repo/record nào khác đang query record trực tiếp (đã grep xác nhận), nhưng
nếu sau này thêm 1 method mới quên pattern này, lỗi sẽ chỉ lộ lúc chạy test/production, không phải
lúc build.

## 3. `ILeaderboardStore.Add/RemoveFrom*` → no-op

Phát hiện trong lúc audit: các method `AddToGlobalLeaderboardAsync`/`RemoveFromGlobalLeaderboardAsync`
(+ country variant) **không có caller nào trong toàn bộ codebase** kể cả trước khi đổi sang SQLite —
chỉ `FetchGlobalRankAsync` được gọi (từ `OsuLoginUseCase`). Redis sorted-set trước đây vì vậy luôn
rỗng, rank fetch luôn trả `null`.

**Quyết định:** `SqliteLeaderboardStore` tính rank bằng SQL `COUNT` trực tiếp trên `UserStats` (live
query, không cần index riêng để giữ đồng bộ) → `Add/Remove*` giữ nguyên trong interface (không xoá,
vẫn implement `ILeaderboardStore` đầy đủ) nhưng thân hàm là `Task.CompletedTask`, có comment giải
thích lý do. Vì `UserStats.Rscore` không bao giờ được cập nhật theo điểm số thật (theo CLAUDE.md, xem
audit bên dưới), rank hiện tại luôn là 1 cho mọi user — hành vi này không đổi so với trước (trước cũng
luôn trả `null`/không có ý nghĩa), chỉ khác ở chỗ giờ query chạy thật thay vì luôn trả null.

## 4. Xoá 3 file Rider `.run/*.xml` trỏ tới docker-compose

`Database.run.xml`, `Database & Server.run.xml` trỏ tới `docker-compose.dev.yml`/`docker-compose.yml`
(đã xoá) → xoá cả 2 file config này, và bỏ dòng `<toRun name="Database" .../>` khỏi
`Debug.run.xml` (compound run config) vì entry đó giờ trỏ tới config không tồn tại.

## 5. `Basil.Infrastructure.Tests` "chạy foreground" — chưa re-verify dưới background thật

CLAUDE.md trước đây khuyến nghị chạy project test này ở foreground vì nó "quan sát thấy bị kill khi
backgrounded" — lý do gốc gắn với Testcontainers/Docker. Đã xoá Docker khỏi project test này (giờ
dùng file SQLite tạm), và trong session này project chạy trong 1 lần `dotnet test` gộp tất cả 6 project
(không explicit background) vẫn pass — nhưng **chưa test riêng với cờ background thật** của công cụ
Bash. Giữ nguyên khuyến nghị "ưu tiên foreground" trong CLAUDE.md, chỉ nới câu chữ (không khẳng định đã
hết vấn đề) — xem CLAUDE.md dòng liên quan.

## 6. Publish 2 RID: chỉ verify thật `win-x64`

Đã chạy thử executable `win-x64` published thật (khởi động, gọi request, xác nhận `basil.db` +
`Mapsets/` tự tạo cạnh exe, migration log chạy đúng). `linux-x64` chỉ verify được bước
`dotnet publish` thành công (build ra output), **chưa chạy thử binary Linux thật** (máy dev là
Windows). Rủi ro thấp vì đây là framework-dependent publish (không cross-compile native code), nhưng
chưa có bằng chứng runtime trên Linux.

## 7. Concurrency (WAL + `Default Timeout=5`) — mới cấu hình, chưa có test chứng minh

Thiết kế đã bàn kỹ (spec doc, mục "Error handling / concurrency") nhưng **không có test nào tạo tải
ghi đồng thời thật** để xác nhận `busy_timeout` đủ ở quy mô LAN tournament. Đủ tin cậy cho scope hiện
tại (server nhỏ, không phải benchmark), nhưng đây là giả định chưa kiểm chứng bằng test, chỉ bằng
review thiết kế.
