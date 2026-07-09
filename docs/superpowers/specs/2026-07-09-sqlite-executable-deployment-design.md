# SQLite + Executable Deployment — Design

## Bối cảnh

Basil hiện chạy như một container Docker (multi-stage build), phụ thuộc MySQL 8 và
Redis 7 làm datastore. Server chỉ dùng cho LAN tournament, chạy trên máy của người tổ
chức giải — không cần một stack nhiều service. Đổi sang:

- **Deployment**: publish thành executable, không cần Docker/daemon nào. Các thư mục
  dữ liệu (replay, avatar, beatmap, seasonal, faq...) và file DB nằm ngay cạnh thư mục
  chứa executable.
- **Datastore**: MySQL + Redis → SQLite (1 file, embedded, không cần service riêng).

Không có behavior change ở tầng gameplay/protocol — đây thuần là đổi hạ tầng lưu trữ
và cách chạy server.

## Kiến trúc / thành phần

### 1. Persistence layer (MySQL → SQLite)

- Driver: `Microsoft.Data.Sqlite` + Dapper, giữ nguyên pattern hiện tại (mở connection
  theo từng operation qua Dapper, không đổi kiến trúc repository).
- **Connection string** = 1 file path, cộng 2 keyword quan trọng:
  - `Foreign Keys=True` — SQLite tắt FK enforcement theo mặc định mỗi connection; schema
    có FK (`UserStats_Users_Id_fk`...) nên cần bật lại để giữ integrity tương đương MySQL.
  - `Default Timeout=5` — map sang `busy_timeout` phía SQLite. Server này *cố tình*
    multithreaded (`MatchSession.Lock` chỉ serialize *trong* 1 match, không serialize
    giữa các match/score-submission khác nhau) nên ghi đồng thời từ nhiều match là bình
    thường. Không set cái này → `SQLITE_BUSY`/"database is locked" throw ngay khi có 2
    writer đụng nhau; có timeout → writer thứ 2 đợi thay vì crash.
- **WAL mode**: `SqlMigrationRunner` chạy `PRAGMA journal_mode=WAL;` một lần lúc khởi
  động, trước khi áp migration. Setting này persist vào file DB (header), không phải
  per-connection, nên chỉ cần set 1 lần trong vòng đời file DB.
- **Migration engine**: `dbup-mysql` → `dbup-sqlite`. Viết lại
  `001_base.sql` sang cú pháp SQLite:
  - `int auto_increment primary key` → `INTEGER PRIMARY KEY AUTOINCREMENT`
  - Bỏ `unsigned`, precision kiểu `float(6,3)` (SQLite dynamic-typed, cột affinity thôi,
    những modifier này bị ignore/lỗi cú pháp) — giữ affinity gần nhất (`REAL`, `INTEGER`).
  - `ON DUPLICATE KEY UPDATE` (dùng ở đâu đó trong 8 repo có SQL riêng) →
    `INSERT ... ON CONFLICT(...) DO UPDATE SET ...`
  - Giữ nguyên tên bảng/cột PascalCase, giữ nguyên toàn bộ FK/unique constraint/index.
- **Repository rename**: 14 lớp `MySqlXxxRepository` trong
  `Basil.Infrastructure/Persistence/Repositories/` → `SqliteXxxRepository`. 8 lớp có SQL
  MySQL-only cần viết lại thật sự (`UserRepository`, `MapsetRepository`,
  `ScoreSubmissionPersistence`, `ScoreRepository`, `MailRepository`,
  `IngameLoginRepository`, `MatchPersistenceRepository`, `ClientHashRepository`); 6 lớp
  còn lại (`ChannelRepository`, `LogRepository`, `RatingRepository`,
  `RelationshipRepository`, `StatsRepository`, `MapRepository`) chỉ đổi connection type,
  SQL không cần sửa.
- **Bỏ 2 workaround MySQL-only** ghi trong CLAUDE.md: `TreatTinyAsBoolean=false` (không
  còn `tinyint(1)` ambiguity) và `CAST(ApiKey AS CHAR(36))` (SQLite không suy luận nhầm
  kiểu `Guid`).
- `DatabaseOptions`: bỏ `Host/Port/User/Password/Name`, thay bằng 1 property `Path`
  (default `"basil.db"`, relative path — xem mục Storage bên dưới về cách anchor).

### 2. Bỏ Redis hoàn toàn

- `IWebSessionStore` / `RedisWebSessionStore` / `WebSessionOptions`: **xoá hẳn** (interface,
  impl, options, test, DI registration). Không có caller nào ngoài chính nó và DI — đây
  là scaffolding cho một web-login flow chưa được xây, xoá đi, thêm lại khi cần.
- `ILeaderboardStore`: **giữ interface nguyên vẹn** (vẫn được `OsuLoginUseCase` gọi qua
  `FetchGlobalRankAsync`), nhưng đổi implementation:
  - `SqliteLeaderboardStore` (thay `RedisLeaderboardStore`): `FetchGlobalRankAsync` /
    `FetchCountryRankAsync` chạy 1 SQL `COUNT` query live trên `UserStats` (+ join `Users`
    cho country) thay vì đọc sorted-set.
  - `AddToGlobalLeaderboardAsync` / `AddToCountryLeaderboardAsync` / `RemoveFrom*`: hiện
    tại **không có caller nào** trong codebase (chỉ Fetch được gọi) — sorted-set Redis
    thực chất luôn rỗng, rank fetch luôn trả null. Với SQLite, rank tính live từ
    `UserStats` nên không cần một index riêng để giữ đồng bộ nữa → implement thành no-op
    (`Task.CompletedTask`), kèm 1 dòng comment giải thích lý do (rank luôn live-query,
    không có state riêng để add/remove).
- Xoá package `StackExchange.Redis`, class `RedisOptions`, cả folder
  `Basil.Infrastructure/Redis/`.

### 3. Storage paths — cạnh executable, fixed cứng (không còn configurable)

- `StorageOptions` **không còn bind từ configuration/env nữa** — bỏ hẳn
  `Storage__*` env var, bỏ `StorageOptions.SectionName`/`services.Configure<StorageOptions>`.
  5 path (`ReplaysPath`, `AvatarsPath`, `MapsetsPath`, `SeasonalsPath`, `FaqsPath`) là
  hằng số cố định, tên folder phẳng ngay cạnh exe: `Replays/`, `Avatars/`, `Mapsets/`,
  `Seasonals/`, `Faqs/`. Không có cách nào override qua config/env/appsettings nữa —
  luôn đúng 5 folder này, cạnh exe.
- Cách impl: `StorageOptions` giữ nguyên là POCO (để 4 chỗ inject `IOptions<StorageOptions>`
  không phải đổi call site), nhưng DI đăng ký bằng
  `services.AddSingleton(Options.Create(new StorageOptions { ReplaysPath = ..., ... }))`
  với giá trị đã resolve thành absolute path (combine tên folder cố định với
  `AppContext.BaseDirectory`) — không còn `services.Configure<StorageOptions>(configuration...)`.
- File DB SQLite (`basil.db`) cũng đặt ngay cạnh exe, cùng cấp với các folder trên — path
  này **vẫn giữ configurable** qua `DatabaseOptions.Path` (không nằm trong yêu cầu fixed
  cứng của storage paths, giữ nguyên như thiết kế gốc).
- **Anchor point**: `AppContext.BaseDirectory`, không phải
  `Environment.CurrentDirectory`/CWD — đảm bảo đúng bất kể double-click exe hay chạy từ
  terminal ở thư mục khác.
- Logic tạo thư mục (`Directory.CreateDirectory`) ở các route/service hiện tại giữ
  nguyên — chỉ path đầu vào đổi từ relative-to-CWD thành absolute-đã-resolve.

### 4. Deployment / packaging

- Xoá: `Dockerfile`, `docker-compose.yml`, `docker-compose.dev.yml`.
- Publish: **framework-dependent, multi-RID** (`win-x64` + `linux-x64`) — máy chạy cần
  cài sẵn .NET 10 runtime (ASP.NET Core runtime), không self-contained (không bundle
  runtime vào exe).
  ```
  dotnet publish src/Basil.Web -c Release -r win-x64 --self-contained false -o publish/win-x64
  dotnet publish src/Basil.Web -c Release -r linux-x64 --self-contained false -o publish/linux-x64
  ```
- Migration + tạo thư mục dữ liệu vẫn tự chạy lúc startup (code hiện tại ở `Program.cs`
  đã làm việc này, không đổi flow — chỉ đổi connection string/path nguồn).
- `.env.example` viết lại: bỏ biến MySQL/Redis-only (`Database__Host` v.v.) **và bỏ luôn
  biến storage path** (`Storage__ReplaysPath` v.v. — không còn tồn tại, storage path fixed
  cứng, mục 3), giữ biến admin key + server behavior. Vì không còn docker-compose đọc
  file `.env`, file này trở thành tài liệu tham khảo các env var override còn lại, set
  trực tiếp trong shell hoặc 1 file `.env`/`appsettings.*.json` cạnh exe tuỳ người dùng
  chọn.

### 5. Tests

- `Basil.Infrastructure.Tests`: bỏ `Testcontainers.MySql` (và cả package
  `Testcontainers.MySql` trong csproj) → `SqliteFixture` mới dùng file SQLite tạm
  (`Path.GetTempFileName()`-style, xoá lúc `DisposeAsync`), áp migration qua
  `SqlMigrationRunner` giống fixture cũ. Không còn cần Docker daemon cho project test
  này — bỏ luôn caveat "needs Docker" gắn với `Basil.Infrastructure.Tests` trong
  CLAUDE.md.
- Rename file test tương ứng theo class mới (`MySqlXxxRepositoryTests.cs` →
  `SqliteXxxRepositoryTests.cs`, xoá `Redis/RedisWebSessionStoreTests.cs`, đổi
  `Redis/RedisLeaderboardStoreTests.cs` → chỗ test mới cho `SqliteLeaderboardStore`).
- Caveat "chạy `Basil.Infrastructure.Tests` ở foreground, không background" trong
  CLAUDE.md: giữ nguyên ghi chú này nếu vẫn quan sát thấy vấn đề tương tự, review lại
  sau khi impl xong (không phải việc của thiết kế, để verify ở bước implement).

### 6. CI + docs

- `.github/workflows/ci.yml`: bỏ step `docker build`. Thêm step publish cả 2 RID +
  `actions/upload-artifact` cho từng RID (tải được từ mỗi CI run).
- Viết lại `docs/run-deployment.md` (thay hướng dẫn `docker compose up` bằng hướng dẫn
  chạy executable + publish, bỏ phần MySQL/Redis env var, storage path env var, và named
  volume — thay bằng bảng liệt kê 5 folder + DB file cố định cạnh exe).
- Cập nhật `README.md` (phần liên quan tới Docker/MySQL/Redis nếu có) và `CLAUDE.md`:
  - Mục Commands: bỏ `docker compose up`, thêm publish command.
  - Mục Architecture (Dapper/MySqlConnector quirks paragraph): thay bằng ghi chú SQLite
    (`Foreign Keys=True`, `Default Timeout`, WAL) nếu còn quirk đáng nhắc.
  - Mục Tests: bỏ dòng "needs Docker; Testcontainers spins up a real MySQL" ở
    `Basil.Infrastructure.Tests`.

## Error handling / concurrency

- `SQLITE_BUSY` dưới tải ghi đồng thời: giảm thiểu bằng `Default Timeout=5` (busy_timeout
  5s) + WAL. Nếu timeout vẫn hit (rất hiếm ở quy mô LAN tournament), Dapper sẽ throw
  `SqliteException` như bình thường — không thêm retry logic mới, giữ nguyên cách xử lý
  lỗi hiện tại của mỗi use case (không có yêu cầu retry trong scope này).
- Foreign key violations giờ enforce thật (MySQL vốn cũng enforce) — không đổi behavior,
  chỉ đảm bảo SQLite không âm thầm bỏ qua.

## Testing

- Unit/domain/protocol/application/architecture test suites: không đổi (không đụng tới
  MySQL/Redis).
- `Basil.Infrastructure.Tests`: viết lại theo SQLite fixture (mục 5).
- `Basil.IntegrationTests`: kiểm tra có phụ thuộc MySQL/Redis trực tiếp không (WebApplicationFactory
  config) — nếu có, đổi sang trỏ SQLite temp file tương tự.
- Verify thủ công (verify skill): publish 1 build win-x64 (hoặc RID đang chạy), chạy từ
  một thư mục rỗng, xác nhận: DB file + 5 folder storage tự tạo cạnh exe, migration chạy
  không lỗi, server start nghe đúng port, một luồng cơ bản (login + xem rank) chạy được.

## Ngoài phạm vi

- Không đổi protocol/gameplay logic, không đổi schema logic (chỉ đổi dialect SQL).
- Không thêm self-contained/single-file publish (đã chọn framework-dependent).
- Không giữ đường Docker song song — xoá hẳn, không làm optional path.
- Không backfill dữ liệu MySQL cũ sang SQLite (server chưa có dữ liệu production cần
  migrate, theo ngữ cảnh dự án hiện tại — LAN tournament tool đang phát triển).
