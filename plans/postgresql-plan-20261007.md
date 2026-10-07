# Kế hoạch: chuyển storage từ SQLite sang PostgreSQL 18

Trạng thái: **chờ duyệt**. Chưa triển khai.

## Mục tiêu

- Thay SQLite bằng PostgreSQL 18 (`localhost:5432`, service `postgresql-x64-18`). Không giữ song song hai DB.
- **Giữ nguyên kiến trúc RAM-first** (AGENTS.md, mục "Memory is the truth; the database is a snapshot of it").
  - Repository cache, `IdentityMap`, `OwnedLists`.
  - `DatabaseBatcher` / `DatabaseWorker`, ghi theo batch, chụp tham số lúc gửi lệnh, đọc không batch.
  - Hợp đồng `IXxxRepository` không đổi; Application không biết DB đổi.
- Chỉ đổi `Basil.Infrastructure.Storage` (driver, schema, SQL, migrator, cấu hình), cộng smoke.

## Những gì đổi

| Phần | SQLite hiện tại | PostgreSQL |
|---|---|---|
| Driver | `Microsoft.Data.Sqlite` | `Npgsql` (MIT, driver chính thức), giữ `Dapper` |
| Kết nối | mở file mỗi lần | một `NpgsqlDataSource` singleton (pool có sẵn) |
| Đọc song song | WAL | MVCC, mức `READ COMMITTED` (mặc định), cùng ngữ nghĩa "chỉ thấy cái đã commit" |
| Ghi | 1 transaction/batch, savepoint mỗi lệnh | 1 transaction/batch (xem P5 về savepoint) |
| Tên bảng/cột | `PascalCase` | `snake_case` (PG tự hạ chữ thường tên không quoted); Dapper `MatchNamesWithUnderscores = true` |
| Thời gian | `INTEGER` Unix ms | `timestamptz` (map thẳng `DateTimeOffset`, luôn UTC) |
| Boolean | `INTEGER` 0/1 | `boolean` |
| IP | `TEXT` | `inet` |
| `Objects` của beatmap | JSON trong `TEXT` | `jsonb` |
| Id tự tăng | `AUTOINCREMENT` | `bigint GENERATED ALWAYS AS IDENTITY` (hoặc `integer`, xem P2) |
| Id local beatmap/beatmapset (≥ 1e9) | `MAX(Id) + 1` trong transaction | `SEQUENCE` riêng bắt đầu từ 1 000 000 000 (không đua, không quét bảng) |
| Danh sách trong câu lệnh | Dapper `IN @List` | `= ANY(@List)` (mảng PG) |
| Migration | `PRAGMA user_version` | bảng `schema_version` + `pg_advisory_lock` khi migrate (hai process không migrate cùng lúc) |

## Chính sách đề xuất (cần bạn duyệt từng mục)

**P1. Batch ghi.** Giữ đúng ngưỡng bạn đặt: batch đến hạn khi đủ **100 lệnh ghi** hoặc lệnh cũ nhất đã chờ **50 ms**.
- Trên localhost, mỗi câu lệnh tốn khoảng 0,05–0,1 ms một lượt mạng, nên batch 100 lệnh chạy tuần tự chỉ mất vài ms.
- Giai đoạn 1: chạy tuần tự bằng Dapper trong một transaction.
- Chỉ khi đo thấy chậm mới chuyển sang `NpgsqlBatch` (gửi cả batch trong một lượt mạng).

**P2. Kiểu dữ liệu.**
- Id: `integer` cho mọi bảng, vì domain đang dùng `int`; riêng `scores`, `logins`, `match_events` dùng `bigint` vì tăng nhanh.
- `Permissions` (ulong 64 bit): lưu `bigint`, ép kiểu như hiện nay.
- MD5: `text` hex chữ thường (như cũ, dễ đọc). Có thể đổi sang `bytea` 16 byte nếu bảng scores lớn.
- Enum: `smallint`.

**P3. Ràng buộc trong DB** (bạn vừa đề xuất "validate hoàn toàn trên ứng dụng").
- Đề xuất: DB không có `CHECK`, không có `FOREIGN KEY`, và không có `UNIQUE` nào mà ứng dụng không tự kiểm.
- Giữ `PRIMARY KEY` (định danh), và giữ `NOT NULL` theo đúng kiểu domain (ứng dụng không tạo null được).
- Giữ `UNIQUE` trên `safe_name`, `beatmaps.hash` và `scores.checksum`. Đây là lưới an toàn cho lệnh thử (đổi tên, đổi hash, điểm trùng): chỉ lệnh thử chạm tới chúng, và lệnh thử trả lỗi về nơi gọi chứ không làm tắc batch.
- Cột suy diễn do DB giữ: `safe_name GENERATED ALWAYS AS (lower(replace(name, ' ', '_'))) STORED`, id, timestamp mặc định.

**P4. Lỗi ghi và thử lại** (câu hỏi lần trước, cần bạn chốt).
- Lỗi môi trường: mất kết nối, `IsTransient`, `40001` serialization, `40P01` deadlock, `53300` hết kết nối, `57P01` admin shutdown, lớp `08` connection.
  - Rollback cả batch và thử lại cùng batch, độ trễ tăng dần (50 ms nhân đôi mỗi lần, tối đa 5 s), không giới hạn số lần.
  - Các lệnh ghi đến sau xếp vào batch kế tiếp, không nhảy cóc.
- Lỗi dữ liệu (lớp `22` data exception, `23` constraint, `42` sai SQL) là bug ứng dụng:
  - lệnh thử: trả lỗi về nơi gọi;
  - bản chụp không ai chờ: **đề xuất** bỏ lệnh đó, log Critical, rồi chạy lại phần còn lại của batch. Không dừng toàn bộ việc lưu.
- Object còn lệnh ghi chưa commit thì không bị nhả khỏi cache, để lần nạp sau không đọc trúng bản cũ.

**P5. Savepoint.** Khi lỗi dữ liệu đã là bug (P4), không cần savepoint cho mỗi lệnh (mỗi savepoint tốn hai lượt mạng).
- Batch lỗi do dữ liệu thì chạy lại từng lệnh một để tìm lệnh hỏng.
- Lệnh thử chạy trong savepoint riêng, vì lỗi của nó là kết quả bình thường.

**P6. Độ bền khi commit.** Batch bản chụp dùng `SET LOCAL synchronous_commit = off`.
- Commit không chờ WAL xuống đĩa, nhanh hơn nhiều.
- Nếu máy sập, mất tối đa khoảng 3 × `wal_writer_delay` (mặc định 600 ms) dữ liệu cuối, nhưng không bao giờ hỏng dữ liệu. Hợp với tinh thần "DB là bản chụp".
- Lệnh thử (đăng ký, nộp điểm, đổi tên) giữ `synchronous_commit = on`.

**P7. Pool kết nối** (`NpgsqlDataSource`).
- `Maximum Pool Size = 20`: 1 cho Worker ghi, phần còn lại cho đọc song song. Giới hạn đọc cũ `ProcessorCount × 2` được thay bằng giới hạn pool.
- `Minimum Pool Size = 2`, `Connection Idle Lifetime = 300` (giây).
- PG mặc định `max_connections = 100`, dư cho một server.
- Không cần PgBouncer.

**P8. Index** (chỉ tạo cái truy vấn thật sự dùng).
- `users (safe_name)` unique.
- `restrictions (user_id)`, `relationships` PK `(actor_id, target_id)`, `logins (user_id, timestamp desc)`.
- `beatmaps (beatmapset_id)`, `beatmaps (hash)` unique, `beatmaps (mode)`.
- `matches (ended_at)`, `match_events (match_id)`.
- `scores (user_id)`, `scores (beatmap_hash)`, `scores (match_id, round_number)`, `scores (checksum)` unique.
- Tìm user theo một phần tên (`LIKE '%x%'`): thêm extension `pg_trgm` và index GIN `gin_trgm_ops` trên `safe_name`. Không có nó, mỗi lần tìm là một lần quét cả bảng.

**P9. Bảng cập nhật thường xuyên** (`users`, `user_stats`, `matches`, `rounds`): `fillfactor = 90` để PG cập nhật tại chỗ (HOT update), giảm phình bảng. Autovacuum để mặc định; chỉnh khi đo.

**P10. Migration.**
- Viết lại một baseline mới `001_baseline.sql` cho PG. Chưa có dữ liệu thật nên không cần chuyển dữ liệu từ SQLite.
- Migrator giữ cách "SQL nhúng trong assembly + bảng version", thêm advisory lock.
- Bỏ file `Basil.db` khỏi `DataPaths`.

**P11. Cấu hình và bí mật.**
- `StorageOptions` thêm `ConnectionString`, do `Basil.Host` bind; tên database mặc định `basil`.
- Mật khẩu không nằm trong repo: dùng biến môi trường hoặc `dotnet user-secrets` khi dev.
- App chạy bằng một role riêng `basil` (không phải superuser), chỉ có quyền trên database `basil`.

**P12. Smoke và test.**
- Mỗi lần chạy smoke tạo database tạm `basil_smoke_<guid>`, chạy, rồi `DROP DATABASE`.
- Giữ mọi kiểm tra hiện có. Bỏ hoặc đổi kiểm tra đọc file `Basil.db` bằng `Microsoft.Data.Sqlite` sang đọc bằng Npgsql.

**P13. Ghi song song: lý do chính để chuyển sang PG.**

SQLite chỉ cho một bên ghi tại một thời điểm, nên hiện chỉ có một Worker ghi. PG cho nhiều transaction ghi cùng lúc, nhưng chỉ có lợi nếu Batcher/Worker được đổi để tận dụng.

- **Làn ghi:** Batcher có `N` làn (đề xuất `N = 4`). Mỗi làn có hàng chờ, ngưỡng batch (P1) và kết nối ghi riêng, các làn chạy song song.
- **Chia làn theo "gốc" của dữ liệu:** `lane = hash(gốc) % N`.
  - Gốc là user cho user, credentials, restrictions, relationships, logins, stats.
  - Gốc là trận cho match, rounds, match events, điểm trong trận.
  - Gốc là beatmapset cho set và beatmaps.
  - Còn lại (settings, banners, channels) vào làn 0.
- **Thứ tự:** mọi lệnh của cùng một gốc nằm cùng làn, nên trong một gốc vẫn đúng thứ tự (không nhảy cóc) và cha luôn trước con. Hai gốc khác nhau thì không phụ thuộc thứ tự, vì DB không có FK (P3).
- **Lệnh thử** (đổi tên, tạo user, hash) chạy trên làn của gốc mình. Hai lệnh thử ở hai làn có thể cùng nhắm một tên, nên `UNIQUE` trong P3 là lưới chặn cuối cùng: lệnh thử đến sau nhận lỗi trùng và trả về nơi gọi.
- **Pool:** P7 đổi thành `N` kết nối ghi cộng các kết nối đọc.
- **Lợi ích thực tế:**
  - một batch chậm hoặc đang thử lại ở một làn không chặn các làn khác;
  - nhiều process (nhiều host Basil, công cụ quản trị) cùng ghi được vào một DB;
  - đọc không bao giờ chờ ghi.
- **Lưu ý trung thực:** với khoảng 1k user, một làn ghi trên localhost đã đủ lưu lượng (khoảng vài nghìn lệnh/giây). Nên đặt `N` cấu hình được (mặc định 4) và đo trước khi tăng.

## Các bước triển khai (sau khi duyệt)

1. **Môi trường.** Tạo role `basil` và database `basil` trên PG 18; thêm connection profile cho postgres-mcp để kiểm tra trực tiếp.
   Kiểm: kết nối được bằng `basil`.
2. **Package và kết nối.** Bỏ `Microsoft.Data.Sqlite`, thêm `Npgsql`. `Database` thành `NpgsqlDataSource`; cấu hình Dapper.
   Kiểm: build Storage.
3. **Schema và migrator.** Viết `001_baseline.sql` cho PG (P2, P3, P8, P9) và migrator mới (P10).
   Kiểm: migrate lên database trống, xem schema qua MCP.
4. **Batcher/Worker** theo P4–P7 và P13: làn ghi song song chia theo gốc, retry có backoff, savepoint chỉ cho lệnh thử, `synchronous_commit`, pool.
   Kiểm: build, cộng kiểm tra hồi quy "lệnh ghi hỏng không chặn" và "lệnh ghi đến sau không nhảy cóc".
5. **SQL trong 15 repository:** `snake_case`, `= ANY`, `timestamptz`, `boolean`, `jsonb`, sequence cho id local. Giao agent, kèm spec cụ thể.
   Kiểm: build và grep không còn `Sqlite`.
6. **Smoke** theo P12.
   Kiểm: ALL PASS.
7. **AGENTS.md và tài liệu:** sửa các câu "SQLite's WAL", ghi chính sách P1–P12.

## Câu hỏi cho bạn

1. Thông tin đăng nhập PG 18: user/password superuser để mình tạo role `basil` và database `basil`. Hay bạn tự tạo rồi đưa mình connection string?
2. Duyệt P3: bỏ hết FK/CHECK, chỉ giữ 3 `UNIQUE` làm lưới an toàn cho lệnh thử?
3. Duyệt P4: lỗi dữ liệu của bản chụp thì bỏ lệnh và log Critical, hay dừng toàn bộ việc lưu chờ người xử lý?
4. Duyệt P6: `synchronous_commit = off` cho batch bản chụp (mất tối đa khoảng 600 ms cuối nếu máy sập)?
5. `timestamptz` thay cho Unix ms có ổn không? Nó dễ đọc khi bạn mở DB ra xem, đổi lại SQL phải viết kiểu khác.
6. Duyệt P13: chia làn ghi theo gốc dữ liệu, mặc định 4 làn?
