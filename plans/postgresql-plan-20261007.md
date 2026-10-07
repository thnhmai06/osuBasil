# Kế hoạch: chuyển storage từ SQLite sang PostgreSQL 18

Trạng thái: **chính sách đã duyệt 2026-10-07; chưa triển khai code**. Đã có sẵn role `basil` và database `basil` trên
PG 18 local.

## Vì sao chuyển

SQLite chỉ cho **một bên ghi** tại một thời điểm, nên cả server chỉ có một luồng ghi. PostgreSQL cho nhiều transaction
ghi cùng lúc. Kế hoạch này đổi lớp storage để ghi song song (P13); không chỉ thay driver.

## Mục tiêu

- Thay SQLite bằng PostgreSQL 18 (`localhost:5432`, service `postgresql-x64-18`). Không giữ song song hai DB.
- **Giữ kiến trúc RAM-first** (AGENTS.md, mục "Memory is the truth; the database is a snapshot of it").
  - Cache (`IdentityMap`, `OwnedLists`), chụp tham số lúc gửi lệnh, đọc không batch.
  - Lưu không chờ DB; lệnh thử (try-operation) chờ DB.
  - Hợp đồng `IXxxRepository` không đổi; Application không biết DB đổi.
- Kiến trúc Batcher/Worker **được đổi** để tận dụng PG (P13). Mọi thay đổi chỉ nằm trong
  `Basil.Infrastructure.Storage`, smoke và AGENTS.md.

## So sánh

| Phần | SQLite hiện tại | PostgreSQL |
|---|---|---|
| Driver | `Microsoft.Data.Sqlite` | `Npgsql` 10.0.3 (MIT, driver chính thức), giữ `Dapper` |
| Kết nối | mở file mỗi lần | `NpgsqlDataSource` singleton (pool có sẵn) |
| Đọc song song | WAL | MVCC, `READ COMMITTED`: vẫn "chỉ thấy cái đã commit" |
| Ghi | 1 hàng chờ, 1 transaction/batch, savepoint mỗi lệnh | `N` làn song song, 1 transaction/batch mỗi làn, savepoint chỉ cho lệnh thử |
| Tên | `PascalCase` | `snake_case`; Dapper `DefaultTypeMap.MatchNamesWithUnderscores = true` |
| Thời gian | `INTEGER` Unix ms | `timestamptz` (UTC) |
| Boolean / IP / JSON | `INTEGER` / `TEXT` / `TEXT` | `boolean` / `inet` / `jsonb` |
| Id tự tăng | `AUTOINCREMENT` | `integer GENERATED ALWAYS AS IDENTITY` (`bigint` cho `logins`, `match_events`) |
| Id local beatmap/set (≥ 1e9) | `MAX(Id)+1` | `SEQUENCE local_beatmap_ids` / `local_beatmapset_ids` bắt đầu từ 1 000 000 000 |
| Lỗi trùng | `SqliteException` mã 19 | `PostgresException.SqlState == PostgresErrorCodes.UniqueViolation` (`23505`) |
| Migration | `PRAGMA user_version` | bảng `schema_version` + `pg_advisory_lock` |

## Chính sách

**P1. Batch ghi.** Mỗi làn có batch đến hạn khi đủ **100 lệnh ghi** hoặc lệnh cũ nhất đã chờ **50 ms**. Lệnh ghi
mới cho cùng identity thay lệnh cũ tại chỗ.
- Giai đoạn 1 chạy tuần tự bằng Dapper trong một transaction (localhost khoảng 0,05–0,1 ms một lượt mạng).
- Chỉ chuyển sang `NpgsqlBatch` (một lượt mạng cho cả batch) khi đo thấy chậm.

**P2. Kiểu dữ liệu.**
- Id `integer` (domain dùng `int`); `bigint` cho `logins`, `match_events`.
- `permissions`, các mặt nạ quyền và tổng điểm dùng `bigint`; enum và các đếm dùng `integer`.
- MD5 lưu `text` hex chữ thường; `objects` của beatmap lưu `jsonb`; IP lưu `inet`; thời gian lưu `timestamptz`.

**P3. Ràng buộc DB là lớp bảo vệ cuối cùng.**
- Luật nghiệp vụ nằm ở ứng dụng; toàn vẹn dữ liệu nằm ở DB. Hai lớp tách biệt.
- Giữ `PRIMARY KEY`, `FOREIGN KEY`, `UNIQUE` (`users.safe_name`, `beatmaps.hash`, `scores.checksum`), `NOT NULL`,
  và các `CHECK` thuần toàn vẹn (`settings.id = 1`, id ≥ 1).
- Không đặt `CHECK` mang tính luật nghiệp vụ (ví dụ `ends_at ≥ starts_at`): luật đó do ứng dụng giữ.
- Cột suy diễn do DB giữ: `safe_name GENERATED ALWAYS AS (replace(lower(name), ' ', '_')) STORED`, id, sequence.

**P4. Lỗi ghi.**
- **Lỗi môi trường** (`NpgsqlException.IsTransient`: mất kết nối, timeout, `40001`, `40P01`, `53300`, `57P01`…):
  - rollback rồi chạy lại **cùng batch**, độ trễ 50 ms nhân đôi mỗi lần, tối đa 5 s, không giới hạn số lần;
  - lệnh ghi đến sau của làn đó chờ, không nhảy cóc; các làn khác vẫn chạy.
- **Lỗi lập trình** (sai cú pháp, vi phạm ràng buộc, thiếu điều kiện):
  - lệnh thử thì ném exception về nơi gọi;
  - bản chụp không ai chờ thì log Critical, bỏ lệnh đó, và chạy lại phần còn lại của batch ngay (không chờ).
- Lỗi của lệnh thử luôn trả về nơi gọi, không bao giờ làm hỏng batch (P5).

**P5. Savepoint.**
- Bản chụp chạy không savepoint: lỗi lập trình làm transaction hủy. Worker báo vị trí lệnh hỏng; Batcher bỏ lệnh đó
  và gửi lại phần còn lại.
- Lệnh thử chạy trong savepoint riêng: lỗi trùng hay lỗi ràng buộc của nó là kết quả bình thường, được rollback về
  savepoint, còn batch vẫn commit.

**P6. Độ bền khi commit.**
- Batch chỉ toàn bản chụp: `SET LOCAL synchronous_commit = off`. Commit không chờ WAL xuống đĩa. Nếu máy sập, mất tối
  đa khoảng 600 ms dữ liệu cuối, không hỏng dữ liệu.
- Batch có lệnh thử: giữ `on`.

**P7. Pool kết nối.**
- Một `NpgsqlDataSource`.
- Số kết nối ghi = số làn (P13). Số lệnh đọc đồng thời giới hạn `ProcessorCount × 2`, để một đợt đọc dồn không chiếm
  hết pool.
- Giữ mặc định Npgsql (tối đa 100); PG mặc định `max_connections = 100`. Không cần PgBouncer.

**P8. Index** (chỉ cái truy vấn dùng thật).
- `users_safe_name` (unique), cộng `users_safe_name_trgm` (GIN `gin_trgm_ops`, extension `pg_trgm`) cho tìm user theo
  một phần tên.
- `restrictions (user_id)`, `logins (user_id, timestamp desc)`, `beatmaps (hash)` unique, `beatmaps (beatmapset_id)`,
  `beatmaps (mode)`, `matches (ended_at)`, `match_events (match_id)`.
- `scores (checksum)` unique, `scores (user_id)`, `scores (beatmap_hash)`, `scores (match_id, round_number)` (partial,
  `where match_id is not null`).

**P9. Bảng cập nhật thường xuyên.** `users`, `user_stats`, `matches`, `rounds` dùng `fillfactor = 90` để PG cập
nhật tại chỗ (HOT). Autovacuum giữ mặc định; chỉnh khi đo.

**P10. Migration.**
- `001_baseline.sql` viết lại cho PG. Chưa có dữ liệu thật, nên không chuyển dữ liệu từ SQLite.
- Migrator giữ cách "SQL nhúng + bảng version", migrate dưới `pg_advisory_lock` để hai server không migrate cùng lúc.
- Bỏ `DataPaths.Database` (file `Basil.db`).

**P11. Cấu hình và bí mật.**
- `StorageOptions.ConnectionString` (bắt buộc), do `Basil.Host` bind.
- Mật khẩu không nằm trong repo; dev dùng biến môi trường hoặc `dotnet user-secrets`.
- App chạy bằng role `basil` (không phải superuser).

**P12. Smoke.**
- Mỗi lần chạy tạo database tạm `basil_smoke_<guid>` rồi `DROP` sau khi xong.
- Các kiểm tra đọc thẳng DB chuyển sang Npgsql.
- Giữ mọi kiểm tra hiện có, thêm các kiểm tra ở mục "Kiểm chứng".

**P13. Ghi song song theo làn.**
- `DatabaseBatcher` có `LaneCount = 4` làn. Mỗi làn có hàng chờ riêng (`OrderedDictionary`), ngưỡng batch P1, và vòng
  lặp riêng. Tối đa một batch mỗi làn đang chạy, nên mỗi làn giữ đúng thứ tự.
- Chọn làn theo **gốc** (root) của dữ liệu: `lane = hash(root) % LaneCount`.
  - `Roots.User(id)`: user, credentials, restrictions, relationships (theo actor), logins, user stats, điểm không thuộc
    trận.
  - `Roots.Match(id)`: match, rounds, match events, điểm trong trận.
  - `Roots.Beatmapset(id)`: beatmapset, beatmaps.
  - `Roots.Server`: settings, channels, menu banners.
- Mọi API ghi của Batcher nhận thêm `root`: `EnqueueAsync(root, identity, write)`, `AppendAsync(root, write)`,
  `WriteAsync(root, command)`. `CachedRepository` có `protected abstract object RootOf(T item)`.
- **FK vẫn giữ (P3), vẫn an toàn với nhiều làn:**
  - cha khác gốc (user của một điểm, creator của trận) luôn được tạo bằng lệnh thử `CreateAsync` và đã commit trước
    khi con xuất hiện;
  - cha cùng gốc (trận và rounds, set và beatmaps) nằm cùng làn nên đúng thứ tự.
- `DatabaseWorker` chạy batch của các làn song song, và chạy các lệnh đọc song song.
- Khi tắt server, các làn dừng; batch đang chờ thử lại được trả về đầu hàng chờ, và `StoppedAsync` ghi nốt mọi làn.
  Mỗi làn có khóa riêng (`SemaphoreSlim`) để lúc này không có hai batch cùng làn.
- Nói thẳng: với khoảng 1k user, một làn đã đủ lưu lượng. `LaneCount` là hằng số, chỉ tăng khi đo thấy cần.

## Thiết kế chi tiết (cho agent)

- `Database`: bọc `NpgsqlDataSource.Create(options.ConnectionString)`, có `OpenAsync`, implement `IAsyncDisposable`.
- `DatabaseOperation(identity, Run: Func<NpgsqlConnection, NpgsqlTransaction, Task>)`, với `Awaited` (lệnh thử) và
  `Done`.
- `DatabaseBatch(Operations)`, với `Errors[]` (lỗi của lệnh thử) và `Completion: Task<BatchOutcome>`.
  - `BatchOutcome(Failure, FailedAt)`: `Committed` khi `Failure == null`; `Transient` khi `Failure is NpgsqlException
    { IsTransient: true }`.
- `ReadOperation(Run: Func<NpgsqlConnection, Task>, Fail)`; lệnh đọc trong repository chỉ nhận `connection`.
- Worker chạy một batch:
  - mở kết nối, `BeginTransaction`;
  - nếu không có lệnh thử: `SET LOCAL synchronous_commit = off`;
  - với mỗi lệnh: bản chụp thì chạy thẳng; lệnh thử thì `SaveAsync`, chạy, rồi `ReleaseAsync`, hoặc `RollbackAsync`
    về savepoint và ghi `Errors[i]` (trừ lỗi transient);
  - commit; khi lỗi thì trả `BatchOutcome(exception, vị trí lệnh đang chạy)`.
- Batcher xử lý kết quả của một batch:
  - commit xong: hoàn tất từng lệnh (lệnh thử có `Errors[i]` thì báo lỗi);
  - lỗi transient: chờ backoff rồi gửi lại cùng batch;
  - `FailedAt ≥ 0`: báo lỗi lệnh đó (Critical nếu là bản chụp), bỏ nó, gửi lại phần còn lại;
  - `FailedAt < 0` và không transient (lỗi lúc commit): báo lỗi mọi lệnh.
- Repository:
  - mọi SQL sang `snake_case`;
  - tham số thời gian dùng `DateTimeOffset.ToUniversalTime()`, row DTO đọc `DateTimeOffset`;
  - boolean thật, `@objects::jsonb`, `IPAddress` cho `inet`;
  - id local lấy bằng `nextval('local_beatmap_ids')`;
  - bắt trùng bằng `PostgresErrorCodes.UniqueViolation` thay cho `SqliteErrors.Constraint`.
- Bản nháp code đã viết rồi gỡ ra (schema đã chạy thử sạch 15 bảng, `Database`, migrator, Batcher, Worker) nằm ở
  scratchpad `pg-draft.patch`. Agent dùng làm tham khảo, không áp nguyên.

## Các bước triển khai

Theo `CLAUDE.md`: Claude điều phối và review; **OpenCode làm** (`opencode run --standalone`, worktree riêng). Fallback
lần lượt là Sonnet/Haiku subagent, rồi mới đến Claude. Mỗi bước phải build sạch; mình review từng dòng trước khi
merge.

| Bước | Việc | Agent (OpenCode Go) | Kiểm |
|---|---|---|---|
| 1 | Package `Npgsql`, `StorageOptions.ConnectionString`, `Database`, schema `001_baseline.sql`, migrator, DI, bỏ `DataPaths.Database` | `qwen3.7-plus` | build Storage; migrate database trống; xem schema bằng `psql` hoặc postgres-mcp |
| 2 | Batcher nhiều làn, Worker, `Roots`, `CachedRepository.RootOf` (P4–P7, P13) | `gpt-6-luna` (phần đồng thời khó) | build; review kỹ thứ tự và tắt server |
| 3 | SQL, row DTO và `RootOf` của 15 repository | `deepseek-v4.1-flash` (thao tác cơ học, 2 agent song song chia theo thư mục) | build; grep không còn `Sqlite` |
| 4 | Smoke theo P12 | `mimo-v2.6-flash` | smoke ALL PASS |
| 5 | AGENTS.md, xóa nhắc tới SQLite/WAL, ghi P1–P13 | Claude (tài liệu ngắn) | đọc lại |

Agent hết quota hoặc làm không đạt: chuyển sang Sonnet subagent, rồi Haiku, rồi mới tới Claude.

## Kiểm chứng

- Build sạch: hai project Application Implementations, ba project Infrastructure, `src/Bot/Application`.
- Smoke ALL PASS, gồm cả các kiểm tra mới:
  - ghi song song: hai gốc khác làn ghi xen kẽ, mỗi gốc vẫn đúng thứ tự;
  - lệnh lỗi lập trình bị bỏ, phần còn lại của batch vẫn được lưu;
  - lệnh thử trùng tên trả `false`, batch vẫn commit;
  - tắt server khi đang có hàng chờ: mọi lệnh đều có trong DB;
  - khởi động lại thì nạp lại đúng dữ liệu.
- Quét lỗ hổng gói mới (`dotnet list package --vulnerable`).
- Review từng dòng theo AGENTS.md trước mỗi commit.

## Quyết định đã duyệt

1. Local: superuser `postgres`/`root`; app dùng role `basil` (mật khẩu dev `basil`, có `CREATEDB` cho smoke), database
   `basil`. **Đã tạo.**
2. P3: ràng buộc DB vẫn giữ làm lớp bảo vệ cuối cùng; luật nghiệp vụ và toàn vẹn dữ liệu tách biệt.
3. P4: chỉ thử lại khi lỗi môi trường. Lỗi lập trình: lệnh thử thì ném exception về nơi gọi; bản chụp thì log
   Critical và bỏ lệnh (phương án a).
4. P6: `synchronous_commit = off` cho batch bản chụp.
5. Thời gian lưu bằng `timestamptz`.
6. P13: nhiều làn ghi song song; kiến trúc được đổi để tận dụng PG; việc tối ưu còn lại theo kế hoạch này.
