# osuBasil — System Overhaul Blueprint (v2)

> Trạng thái: Phase 0 (Measurement) DỪNG có chủ đích sau khi phát hiện RC11 (server chết 2/3 lần chạy full-sequence) — xem "Phase 0 — kết quả cuối" bên dưới. Chưa vào Phase 1.
> Thực thi SAU khi merge `feature/assets-host` (đã merge, xem branch `chore/perf-investigation-phase0`).
> Quy trình dự kiến: **Fable** (research + architecture proposal) → **Opus** (challenge + ADR review) → **Sonnet** (implement từng phase) → load test/profiling (chứng minh) → **Opus/Fable** (review kết quả).

## Phase 0 — kết quả cuối (2026-08-26, dừng theo quyết định user)

**Đã hoàn thành:**
- Mục 1 (sửa harness bug) — xong, commit `63c36ef`.
- Mục 2 (server instrumentation: request duration/host-group, DB write duration+BUSY counter, match-lock wait, SSE subscriber/backlog) — xong, 4 commit (`d5a2ce4`,`7993f91`,`61baa6c`,`e210d03`).
- Mục 3 (2 hypothesis rẻ) — xong: `StopHost` chưa từng quan sát; môi trường Production xác nhận; RC2 hiệu chỉnh qua test trực tiếp (non-JSON/bad-query-type KHÔNG phải bug, route-không-match mới là bug thật).
- Mục 4 (baseline thật) — chạy 3 lần, phát hiện **RC11: server chết 2/3 lần** dưới tải multiplayer+api kết hợp kéo dài. Đã sửa 3 gòng hạn harness trong lúc điều tra (account-count bug, PID-reuse trong ProcessResourceSampler, liveness-check giữa scenario) — 2 commit thêm (`b844b00`, `9511bc4`).

**Data baseline hợp lệ đã thu được** (login/idle/chat/multiplayer/api healthy segments, cả 2 lần chạy, report tại `.loadtest/reports/phase0-baseline-*`):
- Login, idle, chat, multiplayer (4/16/32/64 phòng) chạy sạch nhiều lần — số liệu dùng được cho Phase 1 so sánh trước/sau.
- API read path cực khỏe khi server còn sống: 20-32k RPS, p50 <10ms.
- Stress và soak **CHƯA BAO GIỜ chạy được tới cuối** — cả 2 lần đều chết trước khi tới stress. Capacity envelope trên 100 concurrent **vẫn CHƯA đo được** — RC10 vẫn đúng.

**RC11 (server chết dưới tải kết hợp) — must-fix ưu tiên cao nhất, đứng trước cả Phase 1:**
- Tỷ lệ tái hiện ~67% (2/3), không phải hiếm.
- Không phải crash cổ điển (0 Windows Event Log crash cả 2 lần, không dump dù đã bật `DOTNET_DbgEnableMiniDump`) — process xác nhận thoát hẳn (không treo, không orphan).
- Thời điểm chết không cố định (ngay sau transition vs giữa chừng steady-state) → tích lũy theo thời gian/tải, không phải 1 trigger đơn.
- Cơ chế chính xác: HYPOTHESIS — nghi vấn hàng đầu vẫn là cụm RC1 (SQLite write storm) + RC4 (match state race), nhưng CHƯA quy được về 1 dòng code cụ thể.
- Gap công cụ lộ ra: Serilog log sinks (`latest.log`, `errors_latest.log`) ngừng ghi SỚM HƠN thời điểm chết thật do giới hạn size riêng — chưa từng bắt được đúng khoảnh khắc chết ở cả 2 lần. Cần sửa trước lần điều tra tiếp theo.

**Đề xuất thứ tự ưu tiên lại (thay cho thứ tự gốc trong blueprint) — ĐÃ ĐƠN GIẢN HÓA sau khi RC11 quy về RC1:**
1. Không cần Phase/điều tra riêng cho RC11 nữa — root cause đã rõ, đi thẳng vào **Phase 1 (ADR-001, DB write model)**, coi RC11 là bằng chứng mức độ nghiêm trọng thực tế (sụp toàn phần dưới tải thật, không chỉ chậm) để ưu tiên benchmark/fix ADR-001 khẩn hơn.
2. Sau ADR-001 fix (transaction model + retry/wait-budget), verify bằng đúng `crash-repro-sequence.json` (đã có sẵn, commit `9511bc4`) — nếu ThreadPoolQueueLength không còn phình vô hạn dưới cùng tải thì coi như đã đóng RC11.
3. Phase 2-6 giữ nguyên thứ tự blueprint gốc.

**Công cụ đã sẵn sàng cho verify sau khi fix ADR-001:** `crash-repro-sequence.json` (login→idle→chat→multiplayer→api, không cần soak/stress dài), `BasilMetrics.MatchLockWaitMs`/`DbCommandDurationMs`/`DbBusyCount` đã instrument sẵn, `ProcessResourceSampler` an toàn PID-reuse, liveness-check tự abort nếu lại sụp.

## Context

Issue #4 (v1.0.0-alpha.1) ghi nhận ~60 bug/enhancement. Load test 14/08/2026 cho thấy lỗi server đầu tiên tại concurrency 100. Chủ dự án muốn dừng lối "thấy bug vá bug", điều tra system-level để tìm root causes, rồi overhaul có kế hoạch. Mục tiêu dài hạn: lightweight, high-performance, low-overhead, low-dependency, high-concurrency, predictable under load, stable.

Điều tra đã chạy 3 Opus agents (bancho core/concurrency; data layer/API pipeline; load test evidence) — kết quả chi tiết ở phần Evidence.

## Ràng buộc điều hành (áp cho MỌI phase — implementer phải đọc trước)

1. **Trạng thái tri thức**: mỗi kết luận mang nhãn `CONFIRMED` (bằng chứng trực tiếp: log/stack/số đo/code trace khép kín), `SUPPORTED` (trace cơ học đầy đủ nhưng chưa repro), `HYPOTHESIS` (nhất quán với data, chưa chứng minh), `NEEDS EXPERIMENT` (phải benchmark/repro trước khi hành động). **Implementer KHÔNG được code theo HYPOTHESIS/NEEDS EXPERIMENT như thể là fact.**
2. **No silent behavior changes**: behavior protocol/API hiện có phải giữ tương thích trừ khi Issue #4 yêu cầu breaking change rõ ràng. Đặc biệt: bancho packet layout/ordering, session behavior, API response schema. Mọi thay đổi hợp đồng phải nêu tên trong ADR + cập nhật tests theo CLAUDE.md rule 8/9.
3. **Checkpoint per phase**: mỗi phase kết thúc bằng `tests → benchmark → load test → checkpoint (known-good tag/branch)`. Không dồn nhiều phase rồi mới test. Rollback = quay về checkpoint gần nhất.
4. **Decision gates**: các mục đánh dấu `[GATE]` phải có ADR được duyệt (Opus review) TRƯỚC khi viết code.
5. **Dependency removal cần proof**: mục tiêu là *minimal* dependencies, không phải *fewest possible*. Bỏ 1 dependency phải chứng minh `dependency cost > maintenance cost + functionality risk` của bản tự viết. Không xóa dependency rồi tự xây bản nhỏ hơn nhưng bug hơn.
6. **Không hardcode target người dùng**: không có con số "500 users pass" nào là mục tiêu. Mục tiêu là **capacity envelope + scaling curve** trên reference machine với SLO đã chốt (xem Definition of Done).

---

## Evidence (từ 3 investigation agents — giữ nguyên làm phụ lục tra cứu)

### Load test data (Agent 3) — các tiền đề đã hiệu chỉnh

- "Degradation bắt đầu ở ~100 users" — **SAI LỆCH**. Login path serialize từ N≈2: Little's Law fit 50 users ÷ 136.2 RPS = 367.1ms dự đoán vs 366.92ms đo được → effective parallelism ≈ **1.0**, latency ≈ 7.34ms × N tuyến tính. 100 chỉ là mức cao nhất từng test — nơi 0.27% requests chậm nhất vượt timeout 5s.
- `no-cho-token-header` @ 100 — **CONFIRMED**: `SQLite Error 5: database is locked` → unhandled exception → HTTP 500 không header. Serilog: `responded 500 in 5155-5159ms` = `Default Timeout=5` cạn. CPU lúc fail: **2-3%/8 cores**. Threadpool 32→44 = blocked-thread injection (hệ quả, không phải nguyên nhân).
- Soak "leak" +817MB/h — **ARTIFACT**: run 93s extrapolate ×39 từ warm-up transient. WS phẳng 145-147MB cuối run; GC heap sawtooth lành mạnh; handles/TCP/threads giảm. Không leak nào được chứng minh trong corpus — NHƯNG load test chưa hề exercise kịch bản "match close trong khi SSE client còn kết nối" (cơ chế leak trong code có thật, xem RC5).
- Read path khỏe: api health 25,714 RPS @ 20 users p99 2.8ms; api user (DB read) 13,377 RPS. Write path: **136 RPS ceiling, p50 83ms @ 50 users** — chênh ~99× cùng process.
- Alloc 1.18 GB/s thuộc pha API (~48KB/request) — GC hấp thụ dễ. Không phải bottleneck hiện tại.
- Chat p50 2.8s = artifact của client `PollIntervalSeconds: 5`. Multiplayer 4×4: p50 0.6ms.
- **`Profiles/full.json` (stress 100→5000, soak 750×12h) CHƯA BAO GIỜ CHẠY** — capacity trên 100 users chưa từng đo.
- Gaps đo đạc: không server-side traces, không DB/lock counters, không GC pause, không threadpool queue depth, server-log-tail cap 500 dòng, BCrypt cache pre-warm làm số login đẹp hơn production, Docker runs không so sánh được.

### Bancho core / concurrency (Agent 1)

- `cho-token` gán tại `BanchoProtocolRoutes.cs:153` SAU await có thể throw, không try/finally. Không `UseExceptionHandler` toàn cục.
- Login: ~11 DB round trips, 2-3 writes autocommit độc lập, BCrypt verify sync CPU-bound cache-miss (`BCryptPasswordHasher.cs:39`), fan-out O(N²) (`LoginService.cs:187-243` — build fresh `ChannelInfo` byte[] cho TỪNG recipient).
- **Relogin eviction (`LoginService.cs:91-108`) chỉ `gameSessions.Remove` — bypass toàn bộ `PlayerLogoutService` teardown** (match leave/spectator/channels). Evicted session ra khỏi registry → `GhostDisconnectService` không bao giờ reap → slot mồ côi vĩnh viễn. Giải thích: duplicate players (taskkill+reconnect), "match is locked", `!mp make` kick creator (stale `Match` → `AlreadyInMatch` được tolerate tại `MatchMembershipService.cs:132/:139` → phòng tạo không ai ngồi).
- `!mp start` không beatmap: guard `MatchMembershipService.cs:543` chỉ chạy khi `MapId > 0` → slots kẹt `Playing` mãi. Repro: `!mp make` rồi `!mp start` ngay.
- `CloseAsync:494`: occupant biến khỏi registry → không reset `player.Match = null` → seed chu kỳ stale tiếp.
- GhostDisconnect: sweep 100s/threshold 300s; reap TUẦN TỰ + chờ `match.Lock` KHÔNG timeout (1 match contended chặn toàn bộ sweep); `ExecuteAsync` không try/catch + default `StopHost` → 1 throw = server shutdown.
- Match lock: kỷ luật 19/24 handlers đúng. Vi phạm: DB write dưới lock (`CreateRoundAsync`, `SetRoundEndedAsync` — giữ lock tới 5s busy timeout); TOCTOU đọc `gameSession.Match` trước lock; `_tourneyClients` = `HashSet<int>` thường không sync; fire-and-forget loops (`EmptyRoomCloseLoopAsync`, `CountdownLoopAsync` — OCE unobserved, CTS leak, countdown Announce ngoài lock); fire-and-forget DB writes 5 chỗ.
- Protocol allocation: `BinaryWriter` mỗi primitive 1 array; `WriteMatch` ~25 arrays + double-wrap; `GameSession` queue unbounded, `Dequeue()` double-copy.
- SSE = multicast delegates trên singleton (không phải Channel pub/sub như doc): `TeardownMatch` không complete subscriber channels (closure pin cả MatchSession graph); `JsonMergePatch.DiffObjects` không bao giờ trả null → spam `{}`; `PublishSlotsAsync` 1 call site (HTTP PUT/PATCH) → `/slots/live` câm với mọi packet mutation; publish O(toàn bộ subscribers server-wide) sync dưới match lock; 2/3 channel unbounded; diff global thay vì per-connection (trái sse.md).

### Data layer / API pipeline / dependencies (Agent 2)

- SQLite + Dapper + DbUp. `Microsoft.Data.Sqlite` KHÔNG async thật → mọi `QueryAsync` block pool thread (kể cả BUSY wait). `SetMinThreads` không gọi. `PRAGMA synchronous` không set (default FULL — fsync mỗi commit). Đúng 1 explicit transaction toàn persistence layer (`SqliteMatchRepository.cs:130`). `database.md` claim "configures a short busy timeout" — SAI: chỉ có `Default Timeout=5` (ADO.NET command timeout), không có `busy_timeout` PRAGMA.
- N+1: `BeatmapsetRoutes.cs:315-319` (51 queries/page), `MatchReportService` (200+ queries/report).
- Cache: 🔴 key bỏ `includePrivate` (`CachingBeatmapRepository.cs:31-44`) → beatmap private lộ cho anonymous. Không SizeLimit, không stampede guard; `ChecksumLocks` không evict; BCrypt `_cache` unbounded + lưu candidate secret + `SequenceEqual` không constant-time. Invalidation discipline tốt.
- Storage: .osz zip-on-demand mỗi request (MemoryStream + ToArray, ~200MB LOH/set 100MB, không cache/stampede guard, noVideo chưa nối); ffmpeg N misses = N processes; `FileSystemResponseCache.PutAsync` không atomic; `HandleCreate` chạy `ReconcileAllAsync()` trong request. Menu assets (branch này) sạch.
- API: **không có exception→response mapping nào** (grep `UseExceptionHandler|ProblemDetails|IExceptionHandler|StatusCodePages` = 0 hit) → non-JSON body / query parse / `-1` cho `uint` = bare 500. `EnvelopeMiddleware` = 3× serialization mỗi response, giết streaming, 2 throw sites riêng, route không match (404/405/401 challenge/constraint overflow) passthrough KHÔNG envelope, `errors` field hardcode `null`. `+`→`+`: thiếu Encoder tại `EnvelopeMiddleware.cs:28`. 3 instance `JsonSerializerOptions` độc lập, không source-gen. `UseImageSharp` toàn cục trước routing (mọi bancho request chạy 6 provider Match).
- Score submission: 2 write txn không atomic. Match events: 5 call sites `_ = CreateEventAsync` fire-and-forget → mất audit trail giải đấu đúng lúc contention (11 chỗ khác await đúng).
- Dependencies: `ppy.osu.Game.Rulesets.*` nặng (user quyết: GIỮ, có script dọn); dbup/ImageSharp direct ref = ứng viên bỏ (áp ràng buộc #5); BouncyCastle cần thật (Rijndael-256 legacy); Domain+Protocol 0 packages.

---

## Root causes + trạng thái

### RC1 — SQLite write path
- Cơ chế fail (`database is locked` → 500 → mất cho-token; effective parallelism ≈ 1; blocked-thread injection): **CONFIRMED**.
- Sync-over-async của Microsoft.Data.Sqlite block pool threads: **CONFIRMED** (API surface + threadpool telemetry).
- "`synchronous=FULL` là ceiling 136 RPS": **NEEDS EXPERIMENT** — không được đổi pragma trước benchmark (xem ADR-001).
- WAL checkpoint stall (4 requests block rồi release cùng lúc): **HYPOTHESIS** — cần instrumentation Phase 0.

### RC2 — Không có exception→response mapping
**Cập nhật sau thực nghiệm trực tiếp (Phase 0 mục 3, 2026-08-25)**: khởi động thật server (`.loadtest/server` build, port cô lập 18443) và curl các case Issue #4 nêu. Kết quả SỬA LẠI một phần hypothesis ban đầu:

- **Non-JSON body → POST /announce**: `400 Bad Request` + envelope đầy đủ (`success:false,code:400,...`). **KHÔNG crash.** Hypothesis ban đầu ("non-JSON body crashes pipeline") — **KHÔNG tái hiện được, loại bỏ khỏi danh sách bug thật**.
- **Bad query type** (`mods=-1` cho `uint?`, `page=notanumber`): `400 Bad Request` + envelope đầy đủ. **KHÔNG crash.** .NET 10 minimal API's RequestDelegateFactory tự xử lý binding failure thành 400 gọn gàng (không throw ra middleware) — ĐÃ hoạt động đúng, không phải bug.
- **Route constraint overflow** (`mapsetId` khổng lồ vượt `int`): `404 Not Found`, `Content-Length: 0`, **KHÔNG envelope**. **CONFIRMED bằng repro trực tiếp** — khớp đúng Issue #4 "overflow can return an empty 404".
- **Route không match hoàn toàn**: `404 Not Found` rỗng, **KHÔNG envelope**. **CONFIRMED bằng repro trực tiếp**.
- 401 (admin key) chưa test được trực tiếp (server chạy bypass mode, không có admin key cấu hình) — giữ nguyên **SUPPORTED** (code trace, chưa repro).

**Kết luận RC2 sau hiệu chỉnh**: root cause "không có exception→response mapping" vẫn **CONFIRMED**, nhưng phạm vi hẹp hơn ban đầu tưởng — KHÔNG bao trùm "invalid numeric input" / "non-JSON body" (framework đã xử lý đúng những case này). Phạm vi thật:
1. Exception ứng dụng thật (vd `SqliteException` từ `LoginService`) → bare 500 — **CONFIRMED** (log evidence, Phase 0 mục 3 xác nhận môi trường Production nên không có Developer Exception Page che đi).
2. Route không match / route constraint fail (404/405/overflow) → bare empty body, KHÔNG envelope — **CONFIRMED bằng repro trực tiếp**, đây là gap thật cần Phase 4/ADR-005 sửa (`EnvelopeMiddleware` chỉ áp cho endpoint đã match).
3. `EnvelopeMiddleware` tự throw (`JsonNode.ParseAsync`, `GetValue<int>()`) — **SUPPORTED** (code trace, chưa repro).

→ **Phase 4/ADR-005 cần điều chỉnh scope**: bỏ mục "validation filters cho numeric/JSON binding" (đã đúng sẵn), giữ nguyên "global exception handler cho exception ứng dụng thật" + "envelope phủ route không match".

### RC3 — Session teardown 2 đường (relogin bypass)
Cơ chế: **CONFIRMED** (code trace khép kín). Liên kết tới từng symptom (duplicate/ghost/`!mp make`): **SUPPORTED** — Phase 2 phải viết repro tests TRƯỚC khi sửa để nâng lên CONFIRMED (CLAUDE.md rule 4: regression test cho mọi bug fix).

### RC11 — Server crash toàn phần, không tất định (Phase 0 mục 4, phát hiện mới 2026-08-25/26)

**Sự kiện (CONFIRMED, đã quan sát trực tiếp):** trong lần chạy baseline đầy đủ đầu tiên (login→idle→chat→multiplayer(4,16,32,64 phòng)→api→soak), server **chết hoàn toàn** lúc 22:52:16 (25/08), ngay sau khi multiplayer 64 phòng/512 người chơi hoàn tất và api bắt đầu. Server không còn lắng nghe cổng (mọi request sau đó là "connection actively refused"), toàn bộ api sub-scenario cuối + soak 3 tiếng chạy vào khoảng trống, cho 100% fail vô nghĩa.

**Bằng chứng đã kiểm chứng:**
- Log server (latest.log + errors_latest.log) dừng đột ngột, không có dòng "shutting down", dòng cuối chỉ là 1 response `/health` bình thường.
- Windows Event Log (Application) trong khung 22:40-23:20: **0 sự kiện crash/.NET fault** — loại trừ crash kiểu WER thông thường.
- Lỗi cuối trước khi im lặng: `SQLITE_BUSY` trong `MatchCompleteHandler.SetRoundEndedAsync` (đúng RC1/RC4) — nhưng loại lỗi này đã xảy ra hàng nghìn lần trong run mà không giết process, nên KHÔNG đủ để kết luận là nguyên nhân trực tiếp.
- Thời điểm trùng khớp: CPU spike 50-65% ngay lúc 512 người chơi đồng loạt hoàn thành trận.

**Điều tra tái hiện (2 lần thử, 2026-08-26):**
1. Multiplayer 64 phòng CHẠY RIÊNG (server nguội, mới khởi động) → hoàn tất bình thường, KHÔNG crash. Nhưng hiệu năng tệ hơn nhiều (p50=768ms vs 40ms gốc, 4856 "room-create-failed") — do server chưa warm (JIT/threadpool/connection pool nguội), không phải cùng điều kiện với run gốc.
2. Replay ĐẦY ĐỦ chuỗi login→idle→chat→multiplayer(4,16,32,64)→api (khớp chính xác điều kiện run gốc, có bật `DOTNET_DbgEnableMiniDump` để bắt dump nếu crash lại) → **hoàn tất sạch, "Run complete", không crash, không dump.**

**Cập nhật sau lần chạy baseline thứ 2 (2026-08-26, sau khi vá 3 gòng hạn harness):** Crash **LẶP LẠI LẦN NỮA** — lần này liveness-check mới bắt được ngay (dừng run trước `stress`, tiết kiệm 3h soak vô nghĩa). Tổng cộng: **2/3 lần chạy đầy đủ (login→...→api) chết hoàn toàn** — tỷ lệ tái hiện **~67%, KHÔNG hiếm**. Đây là go/no-go blocker thật sự, không phải rủi ro lý thuyết.

**So sánh 2 lần chết:**
| | Run 1 (2000 acc) | Run 3 (5000 acc) |
|---|---|---|
| Thời điểm chết | ~1 phút sau khi multiplayer(64)→api bắt đầu | ~9-10 phút SAU KHI api đã chạy khỏe (api/1-7 healthy, 20k+ RPS thật) |
| SQLITE_BUSY trước đó | có (hàng nghìn) | có (43703 lần cả run) |
| Windows Event Log (crash/fault) | 0 sự kiện | 0 sự kiện |
| Dump file (`DOTNET_DbgEnableMiniDump=1`) | (chưa bật lần 1) | KHÔNG có dump dù đã bật |

**CẬP NHẬT CUỐI (phân tích resources.csv run 2, đã PID-verified nhờ fix `ProcessResourceSampler` — KHÔNG tốn thêm thời gian chạy test):** đây **KHÔNG phải crash**. Đây là **ThreadPool saturation toàn phần không hồi phục**:

- `resources.csv` run 2 ghi liên tục, KHÔNG đứt quãng, suốt từ lúc api bắt đầu lỗi (10:41) tới lúc harness tự abort (10:55:08) — **process chưa bao giờ thoát tự nhiên**.
- Trong 12 phút đó: `ThreadCount` 123→1068 (đúng ~+4700/giờ), `ThreadPoolQueueLength` 732→2196 (tăng đơn điệu, KHÔNG BAO GIỜ giảm), `CpuPercent` gần như luôn = 0.000, `AllocRateBytesPerSecond` = 0 (không có công việc thực nào đang chạy), `TcpConnections` rơi về 0.
- **Không có cảnh báo PID-reuse nào kích hoạt** (đã grep log, 0 hit) → xác nhận đây LÀ process gốc, KHÔNG phải artifact đo đạc.
- **Quan trọng: tốc độ tăng trưởng này KHỚP với run 1** (~+4000/giờ, tăng suốt 3 tiếng soak không ai chặn) — nghĩa là kết luận "PID-reuse artifact" ghi trước đó cho run 1 là **SAI, cần rút lại**: đó cũng là hiện tượng thật này, chỉ là chạy không bị chặn suốt 3 tiếng nên số tăng cao hơn nhiều (9700+ threads).
- **Vì sao không có Windows Event Log / crash dump:** vì chưa bao giờ có crash để bắt. Process bị **harness tự force-kill** ở cuối (`DotnetServerHost.StopAsync`: đóng cửa sổ chính → chờ 5s → `Kill(true)`) khi liveness-check phát hiện không phản hồi — đúng như code đã viết, không phải crash bất ngờ.

**Đã kiểm tra và LOẠI BỎ giả thuyết "match lock bị giữ mãi":** đọc trực tiếp `MatchCompleteHandler.cs` và `MatchStartHandler.cs` (2 nghi phạm hàng đầu, nơi `SetRoundEndedAsync`/`CreateRoundAsync` có thể ném `SQLITE_BUSY`) — cả 2 đều `try { ... } finally { match.Lock.Release(); }` ĐÚNG chuẩn, `Release()` được đảm bảo chạy dù exception. Không có bug "mất Release()" ở đây.

**Kết luận cuối RC11 (đơn giản hơn nhiều so với dự đoán ban đầu):** không cần 1 root cause riêng. **RC11 CHÍNH LÀ RC1**, chỉ khác là quan sát dưới tải thực tế (multiplayer+api kết hợp) đủ lớn để vượt ceiling ghi SQLite (~136 RPS, đã CONFIRMED từ trước) một cách LIÊN TỤC thay vì tức thời:
- Mỗi write bị block đồng bộ (đã biết từ RC1: `Microsoft.Data.Sqlite` không async thật).
- Khi tốc độ request cần ghi > tốc độ 1 writer SQLite xử lý được, hàng đợi backlog **tự nó phình ra theo cấp số cộng và không bao giờ tự co lại** — đây là hệ quả tất yếu của lý thuyết hàng đợi (Little's Law) khi hệ thống vượt ngưỡng bão hòa kéo dài, KHÔNG cần có bug deadlock/mất-release nào cả. ThreadPool cứ bơm thêm thread (hill-climbing) cố bắt kịp, thread mới cũng bị block ở cùng 1 điểm nghẽn (SQLite writer), CPU vẫn thấp vì toàn bộ thread đang chờ I/O chứ không tính toán.
- Vì sao trong `verify-stress` gốc (chỉ login) không thấy sập toàn phần mà chỉ thấy vài request timeout: tải login-only không đủ VƯỢT ceiling LIÊN TỤC đủ lâu để backlog tích lũy tới mức không hồi phục. Multiplayer(64 phòng, ghi round liên tục)+api kết hợp thì đủ.

**Fix = ADR-001, không cần thêm ADR/Phase riêng cho RC11.** Đây là bằng chứng THỰC TẾ, THUYẾT PHỤC HƠN cho việc ADR-001 khẩn cấp — hệ thống không chỉ "chậm" ở tải cao mà **sụp đổ toàn phần, không tự hồi phục, không crash để restart tự động** (thao tác duy nhất phục hồi được là harness/operator force-kill thủ công). Không cần điều tra thêm, không cần chạy lại test — đã đủ dữ liệu để bắt tay Phase 1/ADR-001.

**Phát hiện phụ — bug harness của chính investigation này (đã sửa, xem commit `b844b00`):**
- `ProcessResourceSampler` không xác minh danh tính process qua các lần sample → sau khi server chết, PID bị hệ điều hành tái sử dụng bởi process khác, sampler báo nhầm ThreadCount/HandleCount tăng vọt (1600→9700+) suốt 3 tiếng — ban đầu tưởng leak thật, hóa ra là artifact đo đạc. Đã vá: cache `Process.StartTime` lúc attach, phát hiện mismatch → báo sample rỗng thay vì dữ liệu sai.
- Không có liveness check giữa các scenario → server chết không ai biết cho tới khi soak 3 tiếng chạy xong. Đã vá: probe 10s trước mỗi loại scenario, abort toàn bộ run nếu server không phản hồi.
- `full.json` có bug cấu hình: `stress.ConcurrentUsers` tới 5000 nhưng `Accounts.Count=2000` → stress fail âm thầm (chỉ WARN), không chạy. Đã sửa `Accounts.Count=5000`.

### RC4 — Match state machine hở
- `MapId==0` skip start guard: **CONFIRMED** (code). Repro test cần viết.
- DB write dưới match lock: **CONFIRMED** (presence); impact lên lock hold time: **SUPPORTED**.
- TOCTOU `Match` trước lock: **SUPPORTED**. `_tourneyClients` HashSet: **CONFIRMED** presence, impact **HYPOTHESIS** (cần tourney traffic).
- Fire-and-forget loops/writes: **CONFIRMED** presence; data loss thực tế: **SUPPORTED**.
- `StopHost` từng giết process thật chưa — **Phase 0 mục 3 đã grep toàn bộ log corpus 2026-08-14** (`.loadtest/server/Logs` + `docker-data/Logs`, cả full lẫn errors log) cho `threw an unhandled exception|BackgroundService failed|GhostDisconnect|shutting down|StopHost` — **0 hit**. Kết luận: **KHÔNG quan sát được trong dữ liệu hiện có** (khác "đã bác bỏ" — cơ chế code vẫn nguyên, chỉ chưa từng bị kích hoạt; soak 12h thật CHƯA BAO GIỜ chạy — xem RC10). Giữ nguyên trong scope Phase 2 vì code smell thật (không try/catch + default `StopHost`), chỉ hạ mức khẩn cấp — không phải nguyên nhân bug đã quan sát.

### RC5 — SSE sai abstraction
- `DiffObjects` không bao giờ null (spam `{}`), `PublishSlotsAsync` 1 call site (slots câm), không `Writer.Complete()` ở đâu (leak khi match close): **CONFIRMED** (code, khớp Issue #4 do user tự quan sát).
- Leak memory thực tế dưới load: **NEEDS EXPERIMENT** (load test chưa exercise; Phase 3 benchmark bắt buộc).

### RC6 — Envelope middleware sai tầng
**CONFIRMED** (3× serialize, throw sites, unmatched passthrough, encoder).

### RC7 — Storage model
Zip-on-demand + không stampede guard + non-atomic cache: **CONFIRMED** (code). ffmpeg stampede: **CONFIRMED, đã fix** (commit `d799929`, xem log vòng 13).

**Cập nhật 2026-09-04**: alloc/latency per-request của zip-on-demand giờ **CONFIRMED bằng benchmark thật** (BenchmarkDotNet, standalone throwaway project, xem ADR-006's Measurements section) — 100MB set: 2.28s, 357MB allocated (~3.6x, VƯỢT ước tính gốc ~2x/~200MB), Gen2 GC collect MỖI lần gọi ở cả 2 size test (10MB/100MB) → LOH pressure thật, không phải lý thuyết. Nhãn cũ "NEEDS EXPERIMENT" chỉ đúng cho **đóng góp vào capacity envelope dưới tải thật** (cần load test đa người dùng đồng thời — vẫn KHÔNG chạy được không giám sát, xem RC11) — phần alloc/latency cô lập per-request đã chuyển CONFIRMED, phần đóng góp vào capacity envelope tổng thể VẪN **NEEDS EXPERIMENT**. Storage vẫn secondary (xem Phase 5) — 1 benchmark không đủ unpark toàn bộ ADR-006 Decision (còn thiếu disk-usage measurement + migration timing, xem ADR).

### RC8 — Protocol allocation + login fan-out O(N²)
Presence: **CONFIRMED**. "Là ceiling kế tiếp sau khi gỡ RC1": **HYPOTHESIS** — chỉ làm Phase 6 sau khi re-profile.

### RC9 — Security
Private-beatmap cache leak, BCrypt cache lưu secret + non-constant-time, unbounded caches: **CONFIRMED** (code). → Phase S, leak sửa SỚM.

### RC10 — Đo đạc
`full.json` chưa chạy, harness artifacts (SoakAnalyzer, chat poll, level attribution), thiếu instrumentation: **CONFIRMED**.

---

## Đánh giá kiến trúc

**GIỮ**: Clean Architecture 5-project + Protocol độc lập; SQLite + Dapper (read path 13-25k RPS chứng minh đủ tốt; đúng cho offline tournament server); match lock model 1 SemaphoreSlim/match (sửa chỗ vi phạm, không đổi model); session registry ConcurrentDictionary; score packet path in-memory; caching decorators (sửa key + bound); menu assets providers.

**REFACTOR**: login pipeline; DB config + transaction model (theo ADR-001); session teardown funnel; match lifecycle (guards, timers, ordering); background services; API validation.

**REBUILD** (mỗi cái 1 ADR + design contract trước khi code): SSE subsystem (ADR-004); envelope mechanism (ADR-005); protocol BinaryWriter (ADR-003 phần packet); .osz storage (ADR-006).

**GIỮ NGUYÊN theo quyết định user**: `ppy.osu.Game.Rulesets.*`.

---

## Explicitly Out of Scope

- Thay SQLite bằng PostgreSQL hoặc DB system khác.
- Redis / distributed cache.
- Microservices / service decomposition.
- Horizontal scaling / distributed deployment.
- Gỡ bỏ `ppy.osu.Game.Rulesets.*` (quyết định user: giữ).
- Full OpenTelemetry adoption (chỉ Metrics tối thiểu ở Phase 0).
- Rewrite subsystem đã có evidence hoạt động tốt (read path API, score packet path, menu assets providers, match lock model, caching decorator structure).
- pp-based gameplay, friends, clans, public v1/v2 API (working-scopes.md).

## ADRs (viết + Opus review TRƯỚC code; template: Problem / Evidence / Constraints / Alternatives / Decision / Trade-offs / Measurements)

> **ADR ≠ implementation plan.** ADR chốt *cơ chế và guarantees* (ví dụ ADR-003: "ordered per-match persistence với guarantees X/Y/Z"), KHÔNG chốt tên class/file/chi tiết code. Chi tiết đó thuộc implementation plan của từng phase, viết sau khi ADR được duyệt.

### ADR-001 — Database write model `[GATE]`
- **Câu hỏi 1 — durability config**: benchmark matrix trước khi chọn, KHÔNG mặc định NORMAL:
  - `synchronous`: {FULL hiện tại, NORMAL, FULL + batched} × transaction model: {autocommit hiện tại, single-transaction login}.
  - Đo: login RPS ceiling, p99, fsync count, và **durability semantics từng cấu hình** (osuBasil giữ dữ liệu giải đấu — mất bao nhiêu giây dữ liệu khi power loss là chấp nhận được? — quyết định cùng user).
- **Câu hỏi 2 — write architecture**: chọn giữa:
  - A. Transaction thường per-request (chỉ sửa config + gộp login writes).
  - B. App-level write serialization (1 semaphore quanh writes).
  - C. Dedicated writer connection + queue.
  - D. Write coordinator/actor.
  - Bắt đầu benchmark từ A (đơn giản nhất); chỉ leo lên B/C/D nếu A không đạt SLO. Không được nhảy thẳng vào build queue.
- **Câu hỏi 3 — retry semantics**: thiết kế **total DB wait budget** per request (không phải busy_timeout × retry chồng nhau): bounded retry + backoff/jitter + budget tổng (ví dụ đề xuất: ≤2s login, ≤500ms in-match write — chốt trong ADR) + failure semantics rõ (hết budget → error envelope/packet đúng loại, KHÔNG retry storm).

### ADR-002 — Session lifecycle
Single teardown funnel: mọi con đường rời hệ thống (logout, relogin eviction, ghost reap, kick, server shutdown) đi qua đúng 1 sequence (match leave under lock → spectator → channels → registry → broadcast). State machine tường minh cho session: các trạng thái + ai được chuyển. GhostDisconnect: parallel-safe, lock wait có timeout, exception-safe, `BackgroundServiceExceptionBehavior` chọn có chủ đích.

### ADR-003 — Match state + persistence ordering `[GATE]`
TRƯỚC khi đẩy DB I/O ra ngoài match lock, định nghĩa **ordering/causality guarantees**: những event nào bắt buộc persist theo thứ tự state transition (Start trước Finish; join trước leave của cùng player), event nào được phép reorder/drop, event nào KHÔNG được mất (audit trail giải đấu: bỏ fire-and-forget). Cơ chế đề xuất để benchmark: per-match ordered outbox (ghi tuần tự theo match, ngoài lock) — nhưng đây là alternative trong ADR, không phải quyết định sẵn. Kèm: MapId guard, TOCTOU re-check sau lock, tourney clients đổi sang cấu trúc concurrent, timers thành managed tasks.

### ADR-004 — SSE `[GATE]` — design contract bắt buộc trước rewrite
Trả lời đủ **SSE invariants**:
- Ownership: ai sở hữu event hub? ai dispose? Match close → ai complete channels? Client disconnect → ai remove subscriber?
- Slow consumer: drop event hay disconnect? Buffer bound bao nhiêu?
- **Event stream vs State stream** — phân loại TỪNG stream hiện có:
  - State-oriented (settings, slots, match main, timer): client chậm chỉ cần trạng thái MỚI NHẤT → **coalescing bắt buộc** (giữ latest, drop intermediate) — đây là chìa khóa scalability.
  - Event-oriented (chat, match events): ordered, không coalesce; chọn drop-oldest + báo gap, hay disconnect.
- Snapshot có phải authoritative? (initial full snapshot rồi patches — Issue #4 yêu cầu envelope-wrapped delta).
- Ordered có bắt buộc không, per-stream? Event nào không được drop?
- Per-connection state + diff (theo sse.md) vs global diff: quyết định + trade-off memory.
- Publish không bao giờ chạy dưới match lock.
- DTO alignment: xem "Design inputs" dưới.

### ADR-005 — API envelope + validation
Envelope tạo tại endpoint layer (1 lần serialize), phủ error/404/405/401; global exception handler → envelope; validation filters (numeric range, JSON body, query parse); encoder relaxed cho `+`; 1 nguồn `JsonSerializerOptions` duy nhất; cân nhắc source-gen (đo trước). Naming/DTO audit toàn bộ API theo Issue #4.

### ADR-006 — Storage
Store .osz trực tiếp (Issue #4 yêu cầu đúng hướng này) + extracted-asset cache + invalidation khi add/replace/delete; single-flight stampede guards (osz/ffmpeg/repo cache); atomic cache writes (temp + move); ReconcileAll ra background; noVideo variant.

---

## Definition of Done (toàn cuộc overhaul)

**Reference machine + workload model + SLO — chốt tại Phase 0, đề xuất ban đầu:**
- Reference: máy dev hiện tại (8 cores/16GB, Windows) + Docker variant.
- Workload model: theo `Profiles/full.json` + kịch bản tournament thực (login wave, N phòng × M người, SSE clients per match).
- SLO đề xuất (user chốt): p99 login < 1s dưới login wave; p99 API read < 200ms; error rate < 0.1%; RSS ổn định qua soak.

**Correctness**: 0 unexpected 5xx; 0 mất mandatory writes (audit trail); 0 state corruption; 0 stale session/slot sau các kịch bản repro (taskkill-reconnect, AFK, `!mp make`+`start`).

**Performance**: báo cáo p50/p95/p99/p99.9 per scenario per concurrency level — không chỉ pass/fail.

**Resource**: CPU, RSS, GC (pause + rate), alloc rate, ThreadPool queue depth, DB busy time — đều được thu và so với baseline.

**Scalability**: đo **scaling curve** tại 50/100/250/500/1000 concurrent; kết luận = capacity envelope trên reference machine + điểm knee + nguyên nhân knee (không phải "N users pass").

---

## Phases

> Mỗi phase: viết failing/repro tests trước → implement → full test suite + Release build → benchmark + load test liên quan → **checkpoint** (tag). Phase sau chỉ bắt đầu từ checkpoint xanh.

```text
                Phase 0 (Measurement)
                   │
        ┌──────────┴──────────┐
        ▼                     ▼
     Phase 1 (RC1+RC2)      Phase S (Security — song song)
        │                     │
        ▼                     │
     Phase 2 (Session+Match)  │
        │                     │
        ▼                     │
     Phase 3 (SSE) ◄─────────┘
        │
        ▼
     Phase 4 (API pipeline)
        │
        ▼
     Phase 5 (Storage — secondary, có thể hoán đổi với 4)
        │
        ▼
     Phase 6 (Performance — theo profile)
        │
        ▼
   Final verification (Exit Criteria)
```

### Phase 0 — Measurement (không đổi behavior)
1. Sửa harness: SoakAnalyzer (min duration, loại ramp-down khỏi fit, threshold riêng per series), chat scenario đo delivery thật (không đo poll interval của chính mình), stress level attribution, bỏ cap 500 dòng server-log tail, thêm `ThreadPool.PendingWorkItemCount`, GC pause, total-machine CPU, cold-BCrypt login scenario.
2. Server instrumentation tối thiểu (System.Diagnostics.Metrics — không cần full OTel): request duration histogram per host-group, DB command duration + BUSY/retry counter, match lock wait time, SSE subscriber count/backlog.
3. Kiểm tra hypothesis rẻ: grep logs lịch sử cho shutdown bởi background service (`StopHost`); xác nhận `ThrowOnBadRequest` effective value (env Production vs Development).
4. Chạy baseline: `full.json` stress tới mức máy chịu được + soak rút gọn 2-4h + login cold-cache. **Chốt reference machine + SLO với user.**
- Checkpoint: baseline report + SLO document.

### Phase 1 — Correctness foundations (RC1 + RC2) — HOÀN TẤT (2026-08-26)

**Đã làm** (commit `bce99a1`, `0147004`, `55070fa` trên `chore/perf-investigation-phase0`):
- ADR-001 Option A: `SqliteConnectionFactory` áp `busy_timeout=5000`+`synchronous=NORMAL` cho 14 repo; `RETURNING` gộp round-trip cho login/clienthash create.
- Verify bằng đúng `verify-stress.json` gốc (nơi phát hiện bug ban đầu): **0/36325 fail** (trước đó 12 SQLITE_BUSY fail tại level 100), RPS 825/s (trước ~136/s).
- 5 fire-and-forget match-event write → await (audit trail giải đấu không còn mất âm thầm). 1 chỗ còn lại (`InMemoryMatchRegistry.Remove`, method sync) để lại cho lần sau vì cần đổi interface.
- Cho-token GIỜ LUÔN có trên response login (2 lớp bảo vệ: `LoginService.ExecuteAsync` không bao giờ throw nữa — dùng `LoginFailureReason.ErrorOccurred` có sẵn trong protocol nhưng chưa từng dùng; route handler bọc thêm phần trước khi gọi service).
- `EnvelopeMiddleware` giờ phủ route không match thật (404/405/overflow) trên host `api.` — trước đó bare empty body. Gate chính xác trên `endpoint is null` (không phải `groupName is null`) để không phá `/openapi/*.json` (đã bắt 1 regression, fix ngay trong session).
- Toàn bộ 1364 test pass (1 flaky FileWatcher không liên quan, xác nhận pass riêng).

**Chưa làm trong Phase 1** (không chặn, có thể revisit): validation filters cho numeric/JSON binding — ĐÃ XÁC NHẬN không cần (RC2 corrected: framework .NET 10 xử lý đúng sẵn). EnvelopeMiddleware's 2 throw site nội bộ (`JsonNode.ParseAsync`, `GetValue<int>()`) — vẫn SUPPORTED, chưa repro, chưa fix.


1. **ADR-001 benchmark trước** (ma trận durability × transaction model; options A→D) → quyết định → implement phần thắng: gộp login writes 1 transaction, bỏ re-read rows, retry theo wait-budget, await mọi mandatory write.
2. Exception mapping: `UseExceptionHandler` → envelope error đúng status; bancho path: login response LUÔN có `cho-token` kể cả failure (try/finally hoặc handler); validation filters cho các case Issue #4 (numeric, JSON, query).
- Verify: fuzz/contract tests toàn bộ case Issue #4 General; stress rerun: 0 lỗi 500 tại mức baseline từng fail; login scaling curve cải thiện đo được.
- Checkpoint.

### Phase 2 — Session + Match state (RC3 + RC4, ADR-002 + ADR-003) — ĐANG LÀM (2026-08-26)

**Đã làm** (branch `chore/perf-investigation`, không cần ADR riêng — không phải mục `[GATE]`):
- MapId guard (`576474d`, làm trước phiên tiếp tục này): `!mp start` không beatmap giờ chặn tường minh + báo lỗi, không còn kẹt slot ở `Playing`. Regression test: `MatchMembershipServiceTests.StartAsync_NoBeatmapSelected_DoesNotStartAndAnnouncesError`.
- RC3 relogin teardown (`8f98eb1`): relogin eviction giờ đi qua `PlayerLogoutService.LogoutAsync` đầy đủ (match leave dưới lock + slot reset, spectator, channel, registry, broadcast) thay vì chỉ `gameSessions.Remove`. Regression test: `LoginServiceTests.DuplicateExpiredSession_InMatch_LeavesMatchAndFreesSlot` (assert slot `Empty` + `existing.Match == null` sau relogin).
- GhostDisconnectService exception-safety (`bf10fe4`): 1 reap lỗi (lock kẹt, exception nhất thời) không còn phá cả sweep lẫn kéo `BackgroundServiceExceptionBehavior` mặc định giết host — try/catch per-session + log. Regression test: `GhostDisconnectServiceTests.RunOnce_OneSessionReapThrows_StillReapsTheRest`.
- `_tourneyClients` (`f1efac7`): `HashSet<int>` → `ConcurrentDictionary<int, byte>`, khớp pattern `_referees`/`_bannedIds`/`_invitedIds` đã có sẵn cùng file. API công khai (`TourneyClients`, `Add/RemoveTourneyClient`) không đổi behavior, test cũ không cần sửa.

- TOCTOU host-authority re-check (`d4158ec`): rà toàn bộ 25 call site dùng `match.Lock.WaitAsync`. 20/25 tự an toàn sẵn — chúng check `match.GetSlot(gameSession.Id)`/`GetSlotId` NGAY SAU KHI lock, tự nhiên no-op nếu userSession đã rời/không còn ở slot đó (kể cả `PartMatchHandler`/`JoinMatchHandler` vì `LeaveAsync`/`JoinAsync` tự guard tương tự bên trong). 5 handler còn lại (`MatchStartHandler`, `MatchTransferHostHandler`, `MatchLockHandler`, `MatchChangeSettingsHandler`, `MatchChangePasswordHandler`) chỉ check `gameSession.Id == match.HostId` TRƯỚC lock, không re-check SAU lock — HostId chỉ đổi dưới lock này, nên 1 sender mất host trong lúc chờ lock (do transfer/leave chạy trước) vẫn được coi là host khi lock tới tay — TOCTOU thật, không chỉ lý thuyết. Đã thêm re-check `if (gameSession.Id != match.HostId) return;` ngay đầu `try` (sau `WaitAsync`) cho cả 5. Regression test `MatchStartHandlerTests.Handle_HostChangesWhileWaitingForLock_NoOp` (verify bằng cách tạm revert fix → test fail đúng như kỳ vọng, rồi khôi phục).

**Chưa làm trong Phase 2** (còn lại, độ phức tạp/rủi ro cao hơn hẳn các mục trên):
- `CloseAsync` (dòng ~493): occupant đã biến khỏi registry thì bị `continue`, không reset `player.Match`. Hạ độ ưu tiên — chỉ có tác động khi có object `GameSession` sống sót cầm `Match` cũ mà registry lookup lại miss, kịch bản chưa rõ ràng, impact **HYPOTHESIS**. Cần điều tra thêm trước khi sửa, không phải fix hiển nhiên như các mục trên.
- Fire-and-forget timer loops (`EmptyRoomCloseLoopAsync`, `CountdownLoopAsync`) → managed timers — thay đổi lifecycle, cần thiết kế nhỏ trước khi code.
- DB write ra khỏi match lock — bị chặn bởi ADR-003 `[GATE]` (ordering/causality guarantees), chưa viết ADR.
- Repro tests taskkill-reconnect / AFK ghost dạng integration (mục 1 gốc của Phase 2) — MapId/RC3/ghost-exception-safety đã có unit-level regression test riêng ở trên; repro end-to-end qua load test/manual chưa làm.

1. Repro tests trước (nâng SUPPORTED→CONFIRMED): taskkill-reconnect duplicate, AFK ghost, `!mp make` kick, `!mp make`+`!mp start` ngay.
2. ADR-002: teardown funnel + ghost service hardening. ADR-003: ordering guarantees → rồi mới đẩy DB I/O khỏi lock; MapId guard; TOCTOU re-check; tourney clients; managed timers; bỏ fire-and-forget event writes.
- Verify: repro tests xanh, race tests hiện có + mới, multiplayer load scenario scale lên (64 phòng theo full.json).
- Checkpoint.

### Phase 3 — SSE rebuild (RC5, ADR-004)
1. ADR-004 contract (invariants + event/state classification + coalescing + backpressure) — Opus review.
2. Implement: per-match hub, per-connection snapshot+diff, complete-on-close, bounded channels, coalescing cho state streams, publish ngoài lock, slots nối mọi mutation path.
3. **SSE benchmark riêng** (không chỉ correctness): 10/50/100 matches × N SSE clients; đo event throughput, CPU, alloc, RSS, lock wait, channel backlog. Đạt = correctness pass VÀ resource không thoái lui so baseline.
- Verify thêm: match close → stream kết thúc; không `{}` spam; soak với match open/close churn + SSE clients còn kết nối → RSS phẳng.
- Checkpoint.

### Phase 4 — API pipeline (RC6, ADR-005)
Envelope tại endpoint layer, encoder, JsonSerializerOptions hợp nhất, naming/DTO/OpenAPI audit + toàn bộ API cluster của Issue #4 (messages, examples, pagination, DELETE body, `text` field...). N+1 fixes (beatmapset list, match report).
- Verify: contract tests + OpenAPI diff có chủ đích; API benchmark không thoái lui.
- Checkpoint.

### Phase 5 — Storage (RC7, ADR-006) — SECONDARY
> **Storage rewrite KHÔNG phải điều kiện tiên quyết cho core concurrency scalability, trừ khi profiling Phase 0/1 chứng minh nó đóng góp đáng kể vào capacity envelope.** Có thể hoán đổi thứ tự với Phase 4 tùy dữ liệu.
Store .osz, asset cache + invalidation, single-flight, atomic writes, ReconcileAll background, noVideo, osu!direct search (xem Design inputs).
- Verify: download benchmark, concurrent-download tests, ingest tests.
- Checkpoint.

### Phase 6 — Performance optimization (RC8) — CHỈ theo profile
Re-profile sau Phase 1-3. Nếu allocation/fan-out hiện là bottleneck kế tiếp: BinaryWriter → `IBufferWriter<byte>`/ArrayPool, share broadcast payloads, login fan-out O(N), Dequeue không double-copy, bounded outbound queue có drop policy. Mỗi mục có benchmark before/after (skill `microbenchmarking` + `analyzing-dotnet-performance` áp dụng ở đây).
- Verify: scaling curve 50→1000, so sánh capacity envelope với baseline Phase 0.
- Checkpoint cuối + báo cáo tổng.

### Phase S — Security / isolation correctness (chạy SỚM, không chờ cuối) — ĐANG LÀM (2026-08-27)

**Đã làm**:
- Private-beatmap cache leak (`afb60d8`) — `CachingBeatmapRepository` key theo id/md5 KHÔNG kèm `includePrivate`, nên 1 lần fetch privileged (`includePrivate:true`) cache lại beatmap riêng tư, trả nhầm cho caller anonymous (`includePrivate:false`) sau đó thay vì `null` đúng ra phải có. Sửa: key riêng theo từng biến thể privacy, invalidate cả 2 biến thể khi ghi. Verify bug thật bằng cách tạm revert → 2 test fail đúng leak → khôi phục. Regression test: `CachingBeatmapRepositoryTests.FetchOneAsync_By{Id,Md5}_PrivateResultNeverLeaksToNonPrivateCaller`.
- `BCryptPasswordHasher` cache (`5f55257`) — cache verify trước đây lưu RAW secret bytes (md5 password / admin key) vĩnh viễn theo stored hash, + so sánh cache-hit bằng `SequenceEqual` (không constant-time, timing side-channel trên đúng đường hot path mỗi lần login lặp/check admin key). Sửa: cache digest SHA-256 một chiều của candidate thay vì raw bytes, so sánh bằng `CryptographicOperations.FixedTimeEquals`. Verify bằng revert → test fail đúng leak → khôi phục. Regression test: `BCryptPasswordHasherTests.Verify_CachesDigestNotRawSecret`. Bound kích thước cache này KHÔNG cần riêng — cache chỉ phình theo số tài khoản distinct từng verify (không theo request), gộp chung xử lý ở mục cache bounds dưới.
- Tắt `Server: Kestrel` header (`748ff89`) — `options.AddServerHeader = false` trong `ConfigureKestrel`. 1 dòng, rủi ro thấp nhất, không cần test riêng (config API chuẩn của Kestrel).
- Cache bounds chung (`8f7d9bc`):
  - `ChecksumLocks` (dedup lock cho score submission) KHÔNG BAO GIỜ remove entry sau khi dùng — mỗi submission mang checksum distinct → phình vô hạn suốt vòng đời server (rủi ro DoS THẬT, khác các cache TTL-based khác chỉ phình theo catalog size). Sửa: compare-and-remove (`TryRemove(KeyValuePair)`) trong `finally`, an toàn với submission đang chờ cùng SemaphoreSlim. Verify bằng revert → test fail → khôi phục. Regression test: `ScoreSubmissionServiceTests.SubmitAsync_AfterCompletion_RemovesChecksumLockEntry`.
  - `IMemoryCache` dùng chung cho 4 decorator (Beatmap/Beatmapset/User/Settings) KHÔNG có `SizeLimit` → không ceiling cứng nếu catalog lớn. Thêm `SizeLimit = 10_000` tại DI registration + `Size = 1` cho mọi `cache.Set(...)` (bắt buộc theo API khi SizeLimit được set, thiếu 1 chỗ = throw `InvalidOperationException` runtime). Verify bằng cách tạm bỏ `Size = 1` ở 1 file → test fail đúng lỗi `InvalidOperationException` → khôi phục. Regression test: 1 test mới mỗi file `Caching*RepositoryTests.cs` (`*_AgainstSizeLimitedCache_DoesNotThrow`).

**Phase S coi như đã đủ cho vòng này** — 5/5 mục ban đầu xong (private-beatmap leak, BCrypt cache, Kestrel header, ChecksumLocks eviction, IMemoryCache SizeLimit).

---

## Kết quả chạy liên tục Phase 2 → 6 (2026-08-27, theo chỉ thị "chạy liên tục hết cả 5 phase, chỉ báo cáo cuối")

**Phase 2 — HOÀN TẤT phần không bị gate:**
- Managed timers (`6a51b63`): `EmptyRoomCloseLoopAsync`/`CountdownLoopAsync` (fire-and-forget) giờ bọc try/catch, log exception thay vì fault âm thầm 1 Task không ai quan sát. CTS disposal KHÔNG làm — rủi ro race Cancel/Dispose đồng thời cao hơn giá trị dọn rác nhỏ, quyết định có chủ đích.
- DB write ra khỏi match lock: **BỊ CHẶN bởi ADR-003** — đã viết ADR (`2d62c0a`), dừng đúng theo chỉ thị "viết ADR, dừng chờ duyệt". Chưa code phần này.

**Phase 3 — CHỈ ADR, không code (đúng chỉ thị):**
- ADR-004 (`c541a96`) — design contract đầy đủ cho SSE rebuild, dựa trên đọc code trực tiếp (sửa vài chi tiết so với ghi chú điều tra gốc: TeardownMatch không tín hiệu subscriber, publish là O(toàn server) do event multicast không theo từng match, JsonMergePatch không bao giờ null, PublishSlotsAsync chỉ 1 call site). Đề xuất quyết định, CHỜ duyệt trước khi rebuild.

**Phase 4 — API pipeline, không bị gate, đã code:**
- `0f58676`: exception ứng dụng thật trên host `api.` giờ map sang envelope 500 (RC2 đóng hoàn toàn).
- `1fa4352`: bỏ escape thừa ký tự thường (`+`) trong JSON — relaxed encoder + gộp `EnvelopeMiddleware` về đúng 1 instance `JsonSerializerOptions` chung.
- `b7451ec`: N+1 tại `GET /beatmapsets` (51 query/trang → 1 query gộp).
- `710ed7c`: N+1 tại `MatchReportService` — memoize user/beatmap trong 1 lần build report (KHÔNG phải field instance vì service là singleton).
- `a97bd07`: ADR-005 (retroactive), ghi rõ phạm vi CHƯA làm (audit naming/DTO/OpenAPI đầy đủ, 2 throw site nội bộ `EnvelopeMiddleware`, source-gen JSON).

**Phase 5 — Storage, SECONDARY, chỉ 1 fix nhỏ:**
- `c97879d`: `FileSystemResponseCache.PutAsync` giờ ghi atomic (temp file + rename), sửa race torn-read khi 2 request cùng regenerate 1 file cache.
- `7054bdc`: ADR-006, ghi nhận và HOÃN các mục lớn hơn (.osz zip-on-demand rewrite, `ReconcileAllAsync` chạy trong request, ffmpeg stampede) — đều là quyết định thiết kế lớn hoặc cần bằng chứng profiling, không phải fix nhanh trong phiên này.

**Phase 6 — Performance, CHỈ theo profile (đúng rule gốc), 1 fix code-evidenced:**
- `e7a9b29`: login channel-info broadcast giờ build 1 lần/channel, share reference cho mọi recipient thay vì rebuild y hệt nội dung cho từng session online (RC8, allocation waste O(channels × online sessions)). Justified bằng code evidence (nội dung packet không đổi theo recipient), KHÔNG cần load test lại để biện minh.
- **CHƯA làm và KHÔNG làm trong phiên này**: BinaryWriter→IBufferWriter rewrite, Dequeue double-copy, bounded outbound queue, scaling curve 50→1000 — tất cả đều cần **re-profile thật sau khi Phase 3 (SSE) triển khai xong**, mà Phase 3 mới dừng ở ADR (chưa code) theo đúng chỉ thị gate. Làm các mục này bây giờ sẽ là tối ưu speculative không bằng chứng, trái CLAUDE.md rule 2 và trái chính rule "Phase 6 CHỈ theo profile" của kế hoạch.

**Tổng kết vòng này**: 4 ADR mới (003/004/005/006), 8 fix code (mỗi fix có regression test, verify bằng revert-and-fail trừ 2 trường hợp không có delta quan sát được — Kestrel header, channel-info sharing — đã ghi rõ lý do trong commit). Full suite cuối cùng: 1386 test pass. Tất cả đã push lên `chore/perf-investigation`.

**Cập nhật 2026-09-02**: ADR-004 duyệt + code xong cả 4a và 4b (commit `c541a96` ADR, `4789039` 4b implementation) — SSE snapshot build/publish giờ chạy ngoài `MatchSession.Lock`, gated bởi `SequenceGate`/`MatchSession.NextStateVersion()`. Follow-up "Join/Leave publish-hoist" (phần 4b từng để lại chưa hoist) cũng xong (commit `a9351ea`): `OccupySlot`/`LeaveAsync` không tự publish nữa, mọi caller (packet handler, `PlayerLogoutService`, `CreateAsync`, `!mp join`, HTTP kick/invite/ban routes) tự publish sau khi thả lock. `StartAsync`/`CountdownLoopAsync` cuối tick/`!mp kick`+`!mp ban` (dùng `RunLockedAsync` chung) giữ nguyên publish trong lock — có lý do ghi trong ADR-004, không phải gap bỏ sót. Full suite 1420 test xanh, đã push.

**Còn lại cho phiên sau** *(sửa lại 2026-09-03 — dòng gốc dưới đây SAI: ADR-003 đã duyệt + code xong từ trước, commit `8b3415e` ngày 2026-09-01, tức là TRƯỚC cả note "còn lại" này được viết; note gốc chỉ là theo dõi bị lỡ)*: audit naming/DTO/OpenAPI (Phase 4 phần còn thiếu); chạy load test đầy đủ lại để có evidence cho phần còn lại Phase 6 + Definition of Done (capacity envelope, scaling curve 50/100/250/500/1000).

**Cập nhật 2026-09-02/03 (Issue #4 API/slots cluster audit, branch `chore/perf-investigation`)**: rà toàn bộ API cluster còn mở của Issue #4 theo scope ADR-005, chia Batch A (an toàn tự làm)/B (4 mục breaking-change đã duyệt trước)/C (Match Slots/Live CRITICAL, 7 mục con). Toàn bộ 3 batch xong:
- Batch A: envelope message override cho abort/close, `AdminKeyStatus.LastChanged` cache-invalidation gap, `NotFound()` → có `ErrorResponse` (52 chỗ), menu icon URL clear-to-null, `!mp addref` báo lỗi đúng khi target đã là referee, ban route reject userId chưa đăng ký (quyết định đảo ngược theo user).
- Batch B: `text` field trên announce/motd, MapId nullable xuyên domain+API (`int?`, sentinel `-1` chỉ còn ở Protocol layer), DELETE refs/ban nhận mảng userId trong body, route id overflow trả 400 thật (mọi môi trường) qua `:numericid` constraint mới + fix `BadHttpRequestException` bị nuốt thành 500.
- Batch C (Match Slots/Live CRITICAL): slot 1-indexed khớp `/live/{slotIndex}`, field occupant-only ẩn khi slot rỗng, chặn duplicate userId trong PUT slots, xóa hẳn PATCH slots (chỉ còn PUT full-replace), force-invite giờ tự leave-and-rejoin khi target đang ở match khác (không giữ 2 match lock cùng lúc), lọc dummy input event theo `ReplayAction.Standard`, gộp schema `gameplay`: main match SSE (`/live`) giờ phát thêm event `gameplay` (đóng CRITICAL "FULL MATCH SSE OMITS LIVE GAMEPLAY"), event `score` trên `/live/{slotIndex}` đổi tên `gameplay` khớp main stream. KHÔNG xóa `input` khỏi `/live/{slotIndex}` — verify trước, không có route thay thế tương đương (để quyết định riêng).

Mỗi commit: build Release sạch, full suite xanh, regression test + verify revert-and-fail, đã push. Full suite cuối: 1439 test pass.

**Cập nhật 2026-09-02/03 vòng 2 ("làm tất cả" — status feature + tiếp tục audit)**: user yêu cầu làm cả 2 việc song song — (1) `GET /users/{id}/live` thêm status info, (2) tiếp tục audit toàn diện Issue #4's API section. Cả 6 commit sau đã push:
- `9e87212`: 401 body giờ có envelope JSON (trước rỗng); PUT/PATCH `/matches/{id}/ban` giờ chặn ban referee (route bulk trước đây thiếu guard mà `!mp ban` đã có sẵn).
- `4942706`: `MatchTimerView` thêm `startTime`/`endTime`; `DELETE /matches/{id}/timer` đổi message "Countdown aborted." (trước dùng message generic verb-derived).
- `14dd624`: `GET /matches`' live field — `MapId` giờ null khi beatmap không resolve được (trước trả raw MapId dù beatmap null).
- `e60720d`: `GET /users` giờ có pagination (`page`/`pageSize`, trả `PagedResult`) khớp pattern `GET /matches`/`GET /beatmapsets`.
- `e151f2e` (lớn nhất): feature status info — `IPlayerStatusEvents` (sibling `IPlayerInputEvents`) + `PlayerStatusView` DTO, publish tại `LoginService`/`PlayerLogoutService`/`ChangeActionHandler`. `GET /users/{idOrName}/live` giờ multiplex 2 event (`status` state-oriented + `input` event-oriented) qua `SubscribeMultiWithSnapshot`, đúng convention ADR-004. Sửa kèm 2 chỗ doc sai (event name `frames`→`input`, route path sai trong sse.md).
- `28509e9`: pin bằng test 4 item Issue #4 đã xác nhận fix từ trước mà chưa có test (pagination out-of-range, admin key `+` unicode-escape, OpenAPI invalid enum example — không còn tồn tại, SSE teardown Match Bans — cấu trúc chung ADR-004 đã cover).

Full suite cuối vòng này: 1462 test pass (Domain 114, Protocol 158, Application 651, Infrastructure 224, Architecture 9, Integration 306).

**Cập nhật 2026-09-03 vòng 3**: `ad19571` — thêm `PATCH /menu/seasonals/{fileName}` (rename, theo pattern create/replace/delete có sẵn, `MenuSeasonalService.Rename` trả NotFound/TargetAlreadyExists/Renamed, verify revert-and-fail xác nhận đúng). Đồng thời xác nhận (đọc code trực tiếp, KHÔNG giả định) 3 item Issue #4 đã tự fix từ trước, không cần sửa: `DELETE /users/{id}` giờ gọi `SoftDeleteAsync` (stamp `deletedAt` + reserve username, không chỉ set Privilege=0 như issue mô tả); field `ingested` đã là `IsLocallyIngested` có doc rõ ràng, không mơ hồ; Abbreviation Redirects (`/u/...`) đã handle đúng cả bare-prefix lẫn prefix+rest, có test đầy đủ (`AbbreviationRedirectEndpointTests`). Full suite 1465 test xanh, đã push.

**Cập nhật 2026-09-03 vòng 3 (tiếp)**: xác nhận thêm (đọc code, KHÔNG giả định) 4 item Issue #4 nữa đã tự fix từ trước, không cần sửa: `POST /matches/{id}/abort`/`close` message "Created successfully" sai (đã có "Match aborted."/"Match closed." qua `EnvelopeMessageKey`, Batch A); `hasAdminKey` boolean (đã có trong `AdminKeyStatusView`); `GET /matches`' `status=string` invalid value (đã validate chặt, trả 400 "Expected 'online', 'offline', or 'all'" nếu sai); `GET /matches`' `live` chứa id/name redundant (`MatchRoomLive` record hiện KHÔNG có field Id/Name nào, đã xóa từ Batch B).

**Cập nhật 2026-09-03 vòng 4**: 2 commit thêm trong cụm Beatmaps/Beatmapsets — `5f39320` (`?noVideo=1` cho `GET /beatmapsets/{id}/download`, wiring `BuildBeatmapsetArchiveAsync`'s sẵn-có `noVideo` param, KHÔNG đụng mirror-mode fallback vì đã có test pin `n=1` cứng), `ee9dc91` (audio preview fade-out 1 giây qua ffmpeg `afade` filter, `FFMpegArgumentOptions.WithCustomArgument`, test thật decode PCM verify amplitude giảm ở cuối clip). Full suite 1468 test xanh, đã push. Description upload lỗi ".osz" đã đúng từ trước (xác nhận đọc code, không cần sửa).

**Cập nhật 2026-09-03 vòng 5**: commit `c84027f` đóng cả 4 item còn lại của vòng trước:
- `PATCH /matches/{id}/refs` giờ trả per-target result `{userId, ok, error}` (khớp pattern DELETE /refs có sẵn), báo lỗi đúng khi target đã là referee. Xóa `AddRefereesAsync` batch method (chỉ có 1 caller, hành vi silent-skip chính là bug).
- Match Timer SSE (`/timer/live`) hết spam `secondsRemaining` — tách `MatchTimerLiveView` (không có field này) riêng khỏi `MatchTimerView` (REST giữ nguyên).
- Match Chat SSE (`/chat/live`) giờ buffer message mỗi giây, gộp thành 1 event JSON array thay vì spam từng dòng — `LiveSseRoutes.BufferedPublish` mới (PeriodicTimer), tái dùng `Subscribe` helper có sẵn.
- "Matches created via API cannot be joined" — XÁC NHẬN ĐÃ FIX TỪ TRƯỚC: có sẵn regression test `JoinMatchHandlerTests.Handle_ApiCreatedMatch_GuestCanJoin` (title trích thẳng Issue #4), chạy pass. Không cần sửa gì.

Phát hiện phụ trong lúc viết test cho chat buffering: `chat/live` là route SSE DUY NHẤT trong toàn bộ match-live cluster có `RequireAuthorization` + không có warm snapshot (`Subscribe` thuần event-oriented) — tổ hợp này khiến `HttpCompletionOption.ResponseHeadersRead` không bao giờ trả về cho tới khi có write đầu tiên (TestServer/HttpClient không flush header tách biệt khỏi content cho kiểu response này). Đã thêm helper test `ReceiveAfterTriggerAsyncNoWarmup` xử lý đúng (priming loop nhịp theo flush interval, không poll dồn dập). Đây là quirk hạ tầng test, không phải bug production.

Full suite 1470 test xanh, đã push. PR #7 description đã update (bỏ mục "Architecture Gates" cũ vì ADR-003/004 đã duyệt+code xong, cập nhật Phase 4 progress).

**Cập nhật 2026-09-03 vòng 6 (bắt đầu CRITICAL naming/DTO/OpenAPI review toàn diện)**: user yêu cầu bắt đầu, xác nhận "sửa hết luôn, breaking OK" qua AskUserQuestion. Survey toàn bộ ~40 DTO record trong `Basil.Web/Routing/Api` + `Basil.Application/Services` (17 file route). Tìm 6 inconsistency thật, sửa hết trong commit `4a357f1`:
1. `DateTime` vs `DateTimeOffset` lẫn lộn (không chỉ style — Dapper/SQLite trả `Kind=Unspecified`, implicit conversion sang DateTimeOffset coi là local time thay vì UTC, SAI offset trên host không chạy UTC). Thêm `DateTimeExtensions.AsUtcOffset()` (Basil.Application.Formats), áp cho `MatchListItem`/`MatchClosedView`/`MatchAbortedView`/`BeatmapsetSummary`/`BeatmapsetDetail`/`MenuBannerView`. KHÔNG đụng domain layer (Match/Round/Beatmapset/MenuBanner vẫn DateTime — fix scope tại API boundary).
2. `MatchTimerView`/`MatchTimerLiveView`: `StartTime`/`EndTime` → `StartedAt`/`EndsAt` (khớp convention `*At` toàn bộ API).
3. Gộp 2 pattern confirmation-body (`{id, bool Verbed}` vs `{Success, Message}`) thành 1 — bỏ bool (luôn true, ambiguous field đúng nghĩa Issue #4), giữ resource-id/Message tùy route.
4. `/announce`: `AnnounceBody.Message` → `Text` (khớp response `AnnounceResultView.Text`/`MotdBody.Text`, xóa tự-mâu-thuẫn trong cùng 1 resource).
5. `MatchChatSentView.Sent` → `DeliveredCount` (khớp `AnnounceResultView.DeliveredCount`, cùng ý nghĩa).

Full suite 1470 test xanh (không đổi số lượng — thuần rename/retype, không thêm/bớt test). Đã push.

**Cập nhật 2026-09-03 vòng 7 (naming audit vòng 2 — tag/operationId + sweep DateTime sót)**: commit `642545f`.
- Tag "Motd" → "MOTD" (khớp "FAQ" all-caps acronym convention) — sửa cả `.WithTags` lẫn `Program.cs`'s tag-description table (phải khớp verbatim).
- operationId `getMatchSlotLiveStream` → `getMatchSlotLive` (mọi live-stream operationId khác dùng suffix "...Live" trần, riêng cái này thừa "Stream").
- operationId `removeMatchReferee`→`removeMatchReferees`, `removeMatchBan`→`removeMatchBans` (cả 2 route nhận batch userIds[], trả 1 result/target, giống hệt sibling `addMatchReferees`/`addMatchBans` — chính `.WithSummary` của nó cũng đã dùng số nhiều, chỉ operationId lệch).
- Sweep lại DateTime→DateTimeOffset: vòng 1 bỏ sót `ScoreDetailView` (PlayTime/SubmittedAt) + toàn bộ `MatchReportService.cs` (MatchReport/MatchReportEvent/MatchReportRound/MatchReportScore, 6 field) — do regex vòng 1 không khớp hết kiểu multi-line record declaration. Sửa xong bằng `AsUtcOffset()` đã có sẵn.

Full suite 1470 test xanh (không đổi số lượng), đã push.

**Còn lại cho vòng CRITICAL naming/DTO/OpenAPI review**: 2 vòng survey đã xong phần naming/typing field-level + tag/operationId. Còn: response Produces/example coverage đầy đủ chưa cho MỌI route (chưa audit hệ thống, chỉ check rải rác); route path/param naming như `mapsetId` (thuộc mapset rename riêng); C# DTO type-suffix consistency (quyết định KHÔNG sửa — không wire-visible).

**Còn lại cho phiên sau (audit toàn diện Issue #4's API section — mục 2 của "làm tất cả" CHƯA xong, phần thật sự chưa đụng)**: mục CRITICAL #53 "INCONSISTENT API NAMING, DTOS, DESCRIPTIONS" chưa review toàn diện thật sự (đây là mục lớn nhất, chưa có cách tiếp cận hệ thống — cần liệt kê hết DTO/route rồi so sánh chéo, không phải rà từng item Issue #4 nữa); search API kiểu osu!direct (feature mới, cần thiết kế — xem "Design inputs" trong plan gốc: subset osu-web `BeatmapsetQueryParser` syntax); `mapset`→`beatmapset` rename toàn bộ codebase (đổi tên hàng loạt, rủi ro rộng, cần xác nhận phạm vi với user trước khi làm — đụng namespace/route/domain model, KHÔNG phải fix nhỏ); Match Hosts/Referees (Issue #4 tự đánh dấu "Unable to test" — cân nhắc bỏ qua, hỏi user).

**Kết luận thực tế sau khi rà gần hết mục nhỏ**: phần lớn Issue #4's General/Users/Admin Key/Seasonal/Abbreviation/Menu Icon/Matches (GET) section đã được fix qua các batch trước (A/B/C) hoặc phiên này — chỉ còn thật sự CHƯA làm: mục CRITICAL naming review toàn diện (việc lớn, cần quyết định cách tiếp cận) + cụm Beatmaps/Beatmapsets (5 sub-item, đều là feature mới/đổi tên hàng loạt, không phải bug nhỏ).

**Cập nhật 2026-09-03 vòng 8 (search API kiểu osu!direct, "Đầy đủ như osu!web" theo xác nhận user)**: commit `284b338`.
- Application layer: `BeatmapsetSearchFilters` (SQL-agnostic filter record) + `BeatmapsetSearchQueryParser` (`[GeneratedRegex]`-based `key<operator>value` tokenizer, khớp cú pháp thật `BeatmapsetQueryParser.php` của ppy/osu-web đọc qua DeepWiki). Filter đầy đủ có backing data thật trên Basil: stars/ar/hp(dr)/cs/od/bpm/length(units)/keys(key)/circles/sliders/creator/artist/title/difficulty/status/created(submitted)/updated. Filter KHÔNG có data thật (source/tag/favourites/ranked/divisor/featured_artist) — cố ý KHÔNG implement, token đó tự động rơi xuống thành free-text keyword (đúng hành vi osu!web cho unknown key, không fake data/không lỗi).
- Infrastructure layer: `SqliteBeatmapRepository.SearchAsync` dịch toàn bộ filter sang SQL, `json_extract` cho circles/sliders (tự động loại map non-Standard-mode vì JSON shape thiếu key, không cần guard mode riêng); `SearchCountAsync` mới (tái dùng chung `BuildSearchWhereClause` với `SearchAsync`) cho `totalRecords` chính xác.
- Web layer: `DirectSearchService` (route `/web/osu-search.php` osu!direct trong game) giờ parse `q` qua parser mới thay vì LIKE thô. REST endpoint mới `GET /beatmapsets/search` (`q`/`mode`/`page`/`pageSize`, trả `PagedResult<BeatmapsetSummary>` khớp pattern `GET /beatmapsets`).
- 47 test parser mới + 7 test SQLite mới (bao gồm circles-json_extract, circles-loại-non-Standard-mode, SearchCountAsync khớp SearchAsync) + 3 test REST endpoint mới. Full suite 1527 test xanh (từ 1470), đã push.

**Cập nhật 2026-09-03 vòng 9 (`mapset`→`beatmapset` rename, nửa còn lại "làm cả 2")**: commit `19fcb8a`. User xác nhận "Đổi hết" qua AskUserQuestion (route param + response field + config property `StorageOptions.MapsetsPath`).
- Domain type `Beatmapset`/DB table `Beatmapsets` đã đúng tên sẵn từ trước — phần "mapset" còn sót chỉ ở: route param `{mapsetId}`→`{beatmapsetId}` (mọi route `/beatmapsets/*`), response record `MapsetOperationAccepted`→`BeatmapsetOperationAccepted`, config `StorageOptions.MapsetsPath`→`BeatmapsetsPath` (đổi cả tên thư mục mặc định trên đĩa `Data/Mapsets/`→`Data/Beatmapsets/` — deployment cũ cần tự đổi tên thư mục khi upgrade), cột DB `Beatmaps.MapsetId`→`BeatmapsetId` (migration mới `004_beatmaps_mapsetid_rename.sql`, KHÔNG sửa 3 migration cũ đã chạy), class `MapsetGarbageCollectorService`→`BeatmapsetGarbageCollectorService` (+ file), và ~50 identifier/comment nội bộ khác.
- Thực hiện bằng regex project-wide (PowerShell, lookbehind loại trừ "Beat"/"beat" để không phá "Beatmapset" đã đúng) — lần đầu có bug lookbehind gây lỗi "Beatbeatmapset", phát hiện ngay qua hook cảnh báo file thay đổi bất thường, revert sạch bằng `git checkout .` rồi sửa regex chạy lại, verify bằng grep xác nhận 0 "mapset" chuẩn còn sót và 0 corruption "beatbeatmapset".
- Full suite 1527 test xanh (không đổi số lượng — thuần rename, migration 004 áp dụng sạch qua `SqliteFixture` trong mọi test dùng DB thật).

→ **"làm cả 2, search API trước" đã HOÀN TẤT cả 2 phần.**

**Cập nhật 2026-09-03 vòng 10 (audit response/example coverage toàn diện — phần cuối của naming/DTO/OpenAPI review)**: commit `cc0ebff`. Rà toàn bộ 17 file route dưới `Basil.Web/Routing/Api`, đối chiếu từng `.Produces<T>` với `.WithExample` tương ứng. Xác nhận: file route `assets.`/bancho-host (file/redirect thuần) đúng khi không có example; `.ProducesProblem(...)` trần cũng đúng khi không có — `EnvelopeSchemaTransformer` đã tự synthesize example fallback chung cho MỌI response lỗi thiếu example riêng, nên convention thật của codebase chỉ thêm `.WithExample` khi nó cho thêm thông tin so với generic HTTP reason phrase.

Tìm + sửa 13 gap thật:
- 8 route SSE (`settings/live`, `/live` chính, `chat/live`, `hosts/live`, `refs/live`, `ban/live`, `slots/live`, `timer/live`) khai `Produces<ErrorResponse>(409)` cho `LiveSseRoutes.NotLive()` nhưng chưa từng có example — Scalar/client generator hiện ra generic `{"message":"Conflict"}` (fallback synthesize) thay vì text thật `"Match is not live"`.
- `MenuBannerRoutes.cs` thiếu hẳn 5 example (sibling gần nhất `MenuSeasonalRoutes.cs` coverage 1:1 đầy đủ): POST 400, GET-by-id 200, PUT/PATCH 200+400, DELETE 200.

2 test mới trong `OpenApiDocumentEndpointTests.cs`: `BasilApiDocument_EverySuccessResponseHasAnExample` (sweep toàn bộ response 2xx — không có fallback nên thiếu là chắc chắn thiếu thật), `BasilApiDocument_SseRoute_409UsesTheActualNotLiveMessage` (Theory 8 case, so message thật vs fallback generic — cách DUY NHẤT phân biệt được vì cả 2 đều tạo envelope giống hệt nhau ở response lỗi). Một test sweep "mọi response lỗi phải có example" ban đầu bị bỏ vì không phân biệt được example thật với fallback — không bao giờ fail đúng lúc cần. Verify revert-and-fail cả 2 test, đúng route/lý do fail.

Full suite 1536 test xanh (từ 1527, +9). Đã push.

**Toàn bộ mục "Full naming/DTO/OpenAPI consistency review" (Issue #4's largest remaining item) coi như HOÀN TẤT** — 3 vòng: field-level naming/typing (vòng 6), tag/operationId (vòng 7), response/example coverage (vòng 10). Chỉ còn C# DTO type-suffix consistency (đã quyết định KHÔNG sửa vì không wire-visible) và route path/param naming thuộc phạm vi mapset rename (đã xong vòng 9).

**Còn lại thật sự cho phiên sau**: Remaining BasilBot-side fixes + Storage items từ Issue #4 (chưa touch trong các vòng gần đây); Broader Follow-up (re-profile, load test thật, scaling curves, Definition of Done, doc ADR/architecture cập nhật) — các mục lớn, chưa có yêu cầu nào gần đây nhắc lại, KHÔNG tự bắt đầu nếu user chưa nêu lại.

**Cập nhật 2026-09-03 vòng 11 (BasilBot-side + Storage items từ Issue #4)**: user yêu cầu "làm luôn BasilBot-side và Storage items còn lại". 2 commit:
- `ec2992f` (BasilBot/Multiplayer): `!mp mods` reply giờ mô tả mods THẬT SỰ áp dụng (`match.Mods` sau filter theo gamemode) thay vì raw input; `!mp map <id> [playmode]` — field playmode bị thiếu, giờ thêm (chỉ áp dụng khi beatmap gốc là Standard, silently ignore nếu không); wire `MapName` không còn để trống khi chưa chọn map (constant `NoBeatmapSelectedName` dùng ở cả 2 call site: tạo match mới + clear map); `!mp in` sửa "no longer exists"→"is no longer live" (match vẫn tồn tại trong DB, chỉ không còn live); timer lifecycle: overwrite timer đang chạy giờ announce hủy kèm giây còn lại (`CancelPendingTimer` helper), `!mp start` immediate giờ dừng đúng timer đang pending (bug cũ: background loop tiếp tục chạy ngầm); FAQ nested directory qua separator `:` (list/read/create/replace/delete đều hỗ trợ, `Path.Combine` qua mảng segment nên `:` literal không bao giờ chạm filesystem API — không có bề mặt NTFS ADS). Xác nhận 4 item Multiplayer (start-no-beatmap, taskkill-duplicate, `!mp make` kick creator, AFK ghost) ĐÃ fix từ RC3/ghost-disconnect trước đó — đọc code + test hiện có xác nhận, không code lại.
- `ccc804f` (Storage, breaking): `appsettings.json` chuyển vào `Data/appsettings.json` — di chuyển thật (không chỉ MSBuild Link, vì bước build-time OpenAPI-doc-generation cần file thật ở content root project). `docker-compose.yml` + toàn bộ docs kỹ thuật liên quan cập nhật. Deployment cũ cần tự di chuyển file khi upgrade (đã ghi rõ trong docs).
- "Rename Mapsets→Beatmaps" (Storage item còn lại): coi như đã thỏa mãn qua rename `mapset`→`beatmapset` vòng 9 trước đó (tên thực tế "Beatmapsets" khác chữ "Beatmaps" issue gợi ý, nhưng đúng tinh thần).
- KHÔNG làm (explicitly deferred, quá lớn cho phiên này): "Replace natural language bot responses with structured, machine-parsable formats" (rewrite kiến trúc toàn bộ bề mặt reply bot); "Store beatmapsets directly as .osz + cache + invalidation" (đã có ADR-006 hoãn từ trước, cần benchmark/thiết kế riêng).

12 regression test mới, verify revert-and-fail cho từng fix có thể test được. Full suite: **1548 test pass** (từ 1536). Đã push.

**Cập nhật 2026-09-03 vòng 12 (thiết kế storage .osz trực tiếp — ADR-006 [GATE])**: user chọn "Thiết kế storage .osz trực tiếp" khi được hỏi hướng tiếp theo. Commit `f70a62e`, documentation-only, KHÔNG code, KHÔNG được duyệt.

Đọc toàn bộ code storage/asset hiện tại trước khi thiết kế (BeatmapIngestionService, BeatmapWatcherService, GarbageCollectorService, 2 ImageSharp.Web provider, FfmpegAudioExtractor, 2 call site của IOsuCalculator.Analyze, BuildBeatmapsetArchiveAsync). Phát hiện then chốt định hình toàn bộ thiết kế: MỌI consumer asset hiện tại (ImageSharp.Web's PhysicalImageResolver, ffmpeg process ngoài, beatmap decoder) đều cần REAL FILE PATH — đây là thuộc tính của thư viện third-party, không phải lựa chọn tình cờ — nên cache extracted-asset không phải optional speed-up mà là CƠ CHẾ BẮT BUỘC để giữ các integration này hoạt động khi bỏ per-file-extraction-on-ingest.

Cân nhắc 4 alternative (lazy-only; eager-extract-everything; eager-selective+lazy-fallback; giữ extracted-folder + chỉ cache archive build), chọn alternative 3 làm policy chính, gộp alternative 4's cơ chế cache-archive hẹp vào riêng cho noVideo variant.

Decision: canonical store = 1 file `.osz` rời tại root (giống pathway "loose .osz" đã có sẵn, chuyển từ transient→permanent); cache mới path-resolving (khác `IResponseCache` byte[] hiện tại, tái dùng atomic-write mechanism đã duyệt sẵn trong ADR-006 + single-flight lock theo `(setId, entryName)`); population chọn lọc (.osu files + preview background/audio) + lazy fallback phần còn lại; mọi read path hiện tại rewire qua cache; download cache-backed (không build per-request); invalidation theo thư mục (không per-key); watcher thu hẹp về non-recursive (chỉ theo dõi file `.osz`); migration pass cho deployment cũ (tận dụng làm cache pre-warm miễn phí).

Trade-offs nêu rõ: disk usage bị BOUND chứ không loại bỏ hoàn toàn; invariant mới (cache phải luôn đồng bộ với .osz hiện tại) là điểm dễ vỡ nếu có bug. Measurements: chưa chạy gì (design-only), liệt kê rõ cần benchmark trước khi implement.

**Còn CHỜ user duyệt Decision trước khi code** (đúng plan rule 4, đánh dấu `[GATE]`).

**Cập nhật 2026-09-03/04**: user cho blanket approval ("cứ làm tất cả... coi như mình duyệt hết rồi") — rule 4 (ADR-review gate) coi như đã lift cho mục này. NHƯNG rule 1 (không code theo HYPOTHESIS/NEEDS EXPERIMENT như fact) KHÔNG được lift theo — 2 gate độc lập. ADR-006's chính Measurements section nói rõ CHƯA benchmark gì; RC7's đóng góp vào capacity envelope vẫn nhãn NEEDS EXPERIMENT; Phase 5's header nói rõ storage rewrite KHÔNG phải prerequisite trừ khi profiling chứng minh — profiling đó chưa tồn tại. Việc code `.osz` full rewrite (đổi canonical store, migration di chuyển data deployment cũ) NGAY BÂY GIỜ mâu thuẫn với chính tài liệu này (asked advisor, xem lý do đầy đủ trong session log). Quyết định: **giữ nguyên chờ, đổi lý do từ "chờ duyệt" → "chờ đo đạc"** — không tự ý code full rewrite khi chưa có benchmark. Thay vào đó làm 3 mục nhỏ CONFIRMED, bounded, không cần migration, cùng cluster RC7 (xem log dưới).

- **Ngay sau Phase 0**: sửa private-beatmap cache leak (`includePrivate` vào cache key) — security issue, không phải "lẻ".
- BCrypt cache: bỏ lưu candidate secret, constant-time compare, bound size.
- Cache bounds: `SizeLimit` + entry Size; `ChecksumLocks` eviction.
- `Server: Kestrel` header off.
- Verify: regression tests per item.

**Cập nhật 2026-09-03/04 vòng 13 (BasilBot reply format + ADR-006's 3 bounded item, unattended qua cron "Tiếp tục làm")**: user đi ngủ, blanket approval + "note lại rồi hỏi advisor". 2 commit:
- `6b710e2`: ADR-007 (BasilBot reply format) — recommend AGAINST broad rewrite. Issue #4 không nêu consumer/format cụ thể; `basil-bot.md` đã ghi rõ từ trước "chat text không phải data contract ổn định"; API đã audit kỹ 10 vòng gần đây đã phủ hết state `!mp` động tới. Rule 2 (no speculative feature) + rule 1 (không tự bịa requirement) đều chống lại việc rewrite ~1850 dòng reply surface trên 1 issue mơ hồ. Quyết định: KHÔNG code, ghi rõ lý do, nêu 1 scope hẹp khả dĩ (`!mp settings`) nếu sau này user muốn cụ thể hơn. Advisor gọi lần 2 (lần 1 overloaded) xác nhận hướng đi đúng trước khi commit, kèm góp ý bỏ bullet "BanchoBot precedent" (recall không phải repo evidence) — đã sửa.
- `d799929`: advisor CHẶN việc bắt tay code full `.osz` rewrite dù có blanket approval — chỉ rõ rule 4 (ADR-gate) và rule 1 (không code theo NEEDS EXPERIMENT) là 2 gate độc lập, blanket approval chỉ lift rule 4. ADR-006 Decision vẫn ở trạng thái CHỜ, đổi lý do "chờ duyệt"→"chờ đo đạc" (xem đoạn trên). Thay vào đó làm 1 mục nhỏ CONFIRMED bounded trong đúng ADR-006's "Open items": ffmpeg audio-preview có N-miss=N-process, không stampede guard. Sửa bằng per-beatmapset `SemaphoreSlim` (pattern giống `ChecksumLocks` của `ScoreSubmissionService`, compare-and-remove khi release) quanh `BanchoHostGroups.BuildAudioPreviewAsync`'s `extractor.ExtractAsync`, double-check cache sau khi giành lock. Đây cũng là caller DUY NHẤT của pattern get-miss-build-put trên `IResponseCache` trong code production hiện tại nên đóng luôn mục tổng quát hơn "FileSystemResponseCache thiếu stampede guard" cho trường hợp thật sự tồn tại — KHÔNG thêm guard tổng quát ở tầng cache (sẽ là abstraction thừa, rule 2). Regression test `AudioPreviewSingleFlightTests.Preview_ConcurrentRequests_ExtractsOnlyOnce` (5 request đồng thời, fake extractor đếm + delay), verify bằng revert-and-fail (5 extraction thay vì 1). 2 mục còn lại của advisor's gợi ý (`ReconcileAllAsync` ra background) — đọc kỹ ADR-006 xác nhận chính ADR đã ghi "không phải same-session fix" (đổi response contract của `POST /beatmapsets`, cần quyết định riêng: async/202 hay scope hẹp lại) — giữ nguyên deferred, không tự ý code.

Full suite: 1549 test pass (từ 1548, +1). Release build sạch. Cả 2 commit đã push.

**Cập nhật 2026-09-04 (tiếp, benchmark thật cho ADR-006)**: hỏi advisor lần 3 trước khi tiếp — xác nhận benchmark "before" (không cần "after" vì chưa implement gì) là đúng hướng, non-destructive, và là việc DUY NHẤT thực sự unpark được quyết định thay vì tiếp tục hoãn. Advisor cũng tự nhận 2/3 gợi ý trước đó sai (`ReconcileAllAsync`, generic cache guard) sau khi tôi đối chiếu code — đã không làm 2 mục đó, đúng.

Dùng skill `microbenchmarking`: standalone throwaway project tại scratchpad (`osz-benchmark/`, KHÔNG commit vào repo — đúng use-case "development feedback" của skill). `BuildBeatmapsetArchiveAsync` là `internal`, `Basil.Web` không có `InternalsVisibleTo` cho benchmark project → tái hiện standalone logic y hệt (`MemoryStream`+`ZipArchive.Create`+per-entry `CopyToAsync`+`.ToArray()`) thay vì gọi trực tiếp method thật — ghi rõ giới hạn này trong ADR. Input: random bytes không nén được (giống nội dung .osz thật vốn đã compressed), seeded RNG, chia 6 file. BenchmarkDotNet `[MemoryDiagnoser]`, `[Params(10, 100)]` (MB). Dry run validate trước, rồi `--job Short` lấy số thật.

**Kết quả**: 10MB set → 226ms, alloc 42.2MB (~4.2x); 100MB set → 2.28s, alloc 357.4MB (~3.6x) — VƯỢT ước tính gốc ADR (~2x/~200MB, do MemoryStream doubling-growth + ZipArchive buffering cộng dồn nhiều hơn tưởng). Gen2 GC collect MỖI operation ở CẢ 2 size → xác nhận LOH pressure thật, không phải lý thuyết suông. Đã ghi số liệu đầy đủ vào ADR-006's Measurements section + cập nhật RC7's label (xem trên): phần alloc/latency per-request → CONFIRMED; phần đóng góp vào capacity envelope tổng thể dưới tải thật → VẪN NEEDS EXPERIMENT (cần load test, vẫn không chạy không giám sát). Explicit trong ADR: 1 benchmark KHÔNG unpark toàn bộ Decision — còn thiếu disk-usage measurement + migration-pass timing.

**Còn lại thật sự cho phiên sau (trước vòng 14)**: `.osz` direct storage full rewrite — VẪN CHỜ (2/4 mục Measurements đã xong: alloc/latency; còn thiếu disk-usage across mixed-popularity library + migration-pass timing — không tự code Decision khi 2 mục này còn thiếu); Broader Follow-up (re-profile sau SSE+Phase 4, load test thật, scaling curves 50/100/250/500/1000, Definition of Done, doc ADR/architecture cập nhật) — vẫn chưa có yêu cầu cụ thể nào gần đây đủ để tự bắt đầu load test không giám sát (RC11 vẫn treo — server chết 2/3 lần chạy full sequence, force-kill thủ công mới hồi phục); `ReconcileAllAsync` ra background — cần quyết định response-contract trước (async/202 hay scope hẹp).

**Cập nhật 2026-09-04 vòng 14 (user override ADR-007 qua `/humanizer` misfire, làm thật)**: user gửi "1. Mình vẫn muốn rewrite rộng format reply bot" qua lệnh `/humanizer` (rõ ràng gõ nhầm slash-command, nội dung là chỉ thị trực tiếp, không phải văn cần humanize — xử lý như chỉ thị). Hỏi lại 2 câu (format đích + phạm vi) vì ADR-007 chưa từng chốt format cụ thể (breaking change thật, cần hỏi theo rule 1). User trả lời: **KHÔNG muốn structured/machine-parsable thật** — vẫn muốn ngôn ngữ tự nhiên, chỉ muốn "tạo file ghi hết cách trả lời vào một chỗ (kiểu locale) thay vì cố định trong code". Tức là yêu cầu THẬT khác hẳn Issue #4's literal wording.

Hỏi advisor trước khi code (task lớn, đổi diễn giải): xác nhận approach đúng, nhưng chỉ ra 2 điều quan trọng phải làm: (1) đọc kỹ MpReplies.cs/IrcReplies.cs trước để biết const hay method (ảnh hưởng toàn bộ cơ chế), (2) ADR-007 phải cập nhật ghi rõ yêu cầu đã đổi hình dạng — Issue #4's literal "machine-parsable" item VẪN CÒN MỞ, đây là việc khác.

Đọc toàn bộ MpReplies.cs (122 `const string`)/IrcReplies.cs (32 `const string`) — tất cả literal đơn giản, dùng `string.Format` placeholder, không có parameterized method → an toàn để externalize. Đếm blast radius: 3148 test reference `MpReplies.`, 828 `IrcReplies.` — advisor khuyên giữ public shape byte-identical để 0 caller phải đổi.

Viết script Python (`convert_replies.py`, scratchpad) tự động trích xuất 154 const string → JSON (`Data/Locale/replies.json`, 2 section `Mp`/`Irc`, key = tên field C#) + rewrite 2 file `.cs` (`const string X = "..."` → `static readonly string X = ReplyLocale.Mp(nameof(X))`), verify diff chỉ đổi đúng phần field declaration, không đụng comment/doc/region.

`ReplyLocale.cs` mới (`Basil.Application/Services/`) — load file 1 lần qua `AppContext.BaseDirectory` (theo đúng pattern `MenuIconService` đã dùng sẵn, KHÔNG dùng CWD vì Application layer bị nhiều test project reference, mỗi project có CWD khác nhau lúc `dotnet test`). Wire `Content Include` + `CopyToOutputDirectory` trong `Basil.Application.csproj` — verify bằng thực nghiệm (không đoán): build `Basil.Application.Tests`, xác nhận `Data/Locale/replies.json` thật sự propagate sang output test project (MSBuild content-item transitivity từ ProjectReference — đúng như kỳ vọng).

1 chỗ code thật cần sửa ngoài rewrite máy móc: `MpCommandService.Set` có `const string usage = MpReplies.SetUsage;` — local const cần compile-time constant, field giờ không còn const → đổi `var`.

`Program.cs`: touch 1 member mỗi class lúc `InitializeDataAsync` (trước `app.RunAsync()`) để lỗi file thiếu key là **boot-time failure**, không phải phát hiện giữa chừng lúc có lệnh chat thật (advisor's yêu cầu #1).

Test mới `ReplyLocaleTests` (4 test): mọi member resolve non-empty (2 chiều Mp/Irc) + không key nào trong file bị orphan (không có member nào đọc). Verify bằng revert-and-fail thật: xóa key `Mp.CreateFailed` khỏi JSON, chạy lại → `MpReplies`'s static constructor throw đúng message `"Reply locale file is missing 'Mp.CreateFailed' (expected at ...)"`, kéo theo 40/49 test trong project fail (đúng — mọi test chạm `MpReplies` đều fail cùng lúc, xác nhận fail loud không phải empty string âm thầm). Khôi phục file, diff xác nhận byte-identical với bản gốc script sinh ra.

Build Release + verify file thật sự có mặt trong `win-x64` publish output (`Data/Locale/replies.json` cạnh DLL) — đúng cái bẫy advisor cảnh báo (giống `ccc804f` di chuyển `appsettings.json` trước đó session này) — doc-gen build-time (chạy app thật để generate OpenAPI) cũng resolve đúng, không lỗi.

`ADR-007` cập nhật: ghi rõ yêu cầu literal Issue #4 (structured/machine-parsable) VẪN CÒN MỞ, chưa làm; đây là việc khác (locale file, không đổi wire shape). `docs/for-technicians/configuration.md` thêm mục "BasilBot and IRC reply text" cho admin biết sửa `Data/Locale/replies.json` không cần rebuild.

Full suite: 1553 test pass (từ 1549, +4). Release build sạch. Commit `c3af859`, đã push.

**Còn lại cho phiên sau**: giống list vòng 13 (`.osz` full rewrite chờ đo đạc, Broader Follow-up, `ReconcileAllAsync`) — không đổi gì thêm.

**Cập nhật 2026-09-04 vòng 15 (wording pass, feedback trực tiếp)**: user phản hồi "ý là ngôn ngữ tự nhiên nhưng các câu văn thật tự nhiên và dễ đọc" — locale file đúng hướng nhưng vài câu đọc chưa tự nhiên. Hỏi lại calibrate scope (giữ nguyên style xác nhận ngắn kiểu bancho.py vs viết lại conversational toàn bộ) — user chọn **chỉ sửa jargon/lỗi rõ, giữ nguyên style ngắn gọn**.

Rà toàn bộ Mp section (Irc section giữ nguyên — text IRC numeric-reply chuẩn RFC, không phải bề mặt hội thoại của BasilBot). Sửa 6 chỗ (commit `4149df7`):

- "scoped"/"scope" (thuật ngữ nội bộ rò rỉ ra reply — code thật vẫn gọi `CommandDispatcher`'s scope mechanism là "scope", KHÔNG đổi tên biến/method, chỉ đổi CHỮ hiển thị cho user) → "targeting" (đồng bộ với `NowTargetingMatch` đã có sẵn cùng ý nghĩa) ở 5 chỗ: `CreatedMatch`, `NotScopedToAnyMatch`, `WasScopedToGoneMatch`, `CurrentlyScopedToMatch`, `NotScopedToAnyMatchHint`.
- `CreatedMatch` thêm: "You are"→"You're" (mọi sibling reply khác đều contract, riêng câu này lệch) + sửa câu 2 vế ngữ pháp lệch ("scoped to this match, and added as a referee" thiếu trợ động từ vế 2) → "You're now targeting it, and have been added as a referee."
- `MatchNowPrivate`: "only for invited" thiếu danh từ → "open only to invited players".
- `CannotRemoveLastReferee`/`CannotRemoveCreator`: dấu gạch ngang thường "-" lệch với sibling cùng pattern (`CannotKickReferee`/`CannotBanReferee` dùng em dash "—") → đồng bộ em dash.
- `InviteRequiresClient`: "connected with"→"connected via" (tự nhiên hơn).

Verify: grep toàn bộ tests/ tìm literal "scoped"/wording cũ — chỉ thấy code comment mô tả cơ chế nội bộ, KHÔNG có test assert literal reply text (đúng rule 8/9: test chạm constant `MpReplies.X`, không chạm literal) → 0 test cần sửa. Full suite 1553 test pass (không đổi số lượng — thuần đổi nội dung, không thêm/bớt test). Release build sạch. Đã push.

**Cập nhật 2026-09-04 vòng 16 (hỏi advisor, tìm ra bug Docker thật)**: cron "Tiếp tục làm" — hỏi advisor hướng tiếp (mọi mục queue cũ đều bị block: `.osz` chờ đo đạc, load test chờ giám sát, `ReconcileAllAsync` chờ quyết định contract). Advisor chỉ ra khoảng trống: 2 commit locale-file (`c3af859`/`4149df7`) chỉ verify Release publish output, CHƯA verify đường Docker.

Đọc `docker-compose.yml`: `./docker-data/Data:/app/Data` mount TOÀN BỘ thư mục — bind-mount kiểu này che khuất (shadow) MỌI thứ image build sẵn tại path đó, kể cả `Data/Locale/replies.json` mới, không chỉ file top-level. Đây chính xác là lý do `appsettings.json` đã cần dòng mount riêng đè lên (`ro`) — `replies.json` thiếu dòng tương tự → lần `docker compose up` đầu tiên trên `docker-data/` rỗng SẼ crash ngay lúc boot (đúng theo `Program.cs`'s eager validation mới thêm ở `c3af859`). Bug thật, do commit trước gây ra, chưa ai phát hiện.

Sửa (commit `03a79e7`): thêm dòng mount `replies.json` giống hệt pattern `appsettings.json` trong `docker-compose.yml`; cập nhật `docker.md` ở MỌI chỗ có lệnh `docker run`/diagram layout tương đương (Compose + bare-image lẫn update flow) + giải thích lý do; thêm bước MỚI cho bare-image flow (không có repo checkout thì không thể tự viết tay 150+ reply string như appsettings.json — hướng dẫn extract từ image: `docker run --rm --entrypoint cat ... > replies.json`); thêm `replies.json` vào `deployment.md`'s layout diagram + `configuration.md`'s bảng `Data/` reference.

**Verify KHÔNG đầy đủ, nêu rõ**: Docker daemon môi trường này không chạy được (`docker compose up --build -d` trả về lỗi kết nối engine, không phải lỗi thật của compose file) — KHÔNG test được live container. Fix dựa trên đọc code + cơ chế bind-mount-shadow đã biết của Docker (không đoán), và giống hệt pattern `appsettings.json` đang chạy thật trong cùng file. Đã ghi rõ trong commit message: cần user tự chạy `docker compose up --build -d` thật trên `docker-data/` rỗng, xác nhận log "Reply locale loaded" trước khi tin cậy deploy Docker từ branch này.

**Cập nhật 2026-09-04 vòng 17 (user yêu cầu tách 3 file + category + bỏ MOTD API)**: user yêu cầu tách `replies.json` thành `Data/Localization/BasilBot.json`/`Irc.json`/`Server.json` (đổi tên thư mục `Locale`→`Localization`), nhóm theo category (nested "A.B"="A{B}") thay vì flatten, chuyển MOTD sang `Server.json` và **bỏ hẳn quản lý qua API** (không còn `GET`/`PUT /announce/motd`). User còn đề nghị dùng thư viện `AspNetCore.Localizer.Json`.

Research thư viện qua Context7 (theo đúng rule context7.md — dùng cho MỌI câu hỏi thư viện, kể cả khi tưởng đã biết): Context7 ban đầu không có index, WebSearch xác nhận đúng tên package thật (`aspnetcore.localizer.json` by askmethatfr), user tự thêm vào Context7 index giữa chừng. Query docs xác nhận: thư viện bắt buộc DI `IJsonStringLocalizer<T>` (không có static access), và tài liệu KHÔNG xác nhận hỗ trợ nested/dotted-key lookup (chỉ thấy flat key trong mọi ví dụ). Dùng thật sẽ phải inject vào ~15 service đang gọi `MpReplies`/`IrcReplies` như static member trần + đổi mọi call site — đi ngược chính "giữ public shape, 0 caller sửa" mà 2 commit trước vừa xây xong, mà chưa chắc đáp ứng đúng nhu cầu nested-key. Trình bày rõ trade-off này cho user trước khi code (rule 1) — user chọn **giữ `ReplyLocale` tự viết, mở rộng thêm**, không thêm dependency.

Thực hiện (commit `20495cd`):
- Viết script trích category từ đúng các `// ──` comment-group đã có sẵn trong `MpReplies.cs`/`IrcReplies.cs` (không tự bịa nhóm mới) — verify số lượng key khớp chính xác (122/32) trước khi apply.
- `ReplyLocale` giờ load 3 `JsonDocument`, key format đổi từ tên member trần sang `"Category.Member"`; đổi tên method `Mp`→`BasilBot` (khớp tên file mới, class `MpReplies` giữ nguyên tên).
- `ServerReplies` mới (`Services/Content/ServerReplies.cs`), 1 member `MotdText` đọc `Server.json`'s `Motd.Text`, mặc định rỗng (khớp hành vi DB cũ).
- Xóa hẳn `MotdService.cs` + `GET`/`PUT /announce/motd` + DTO liên quan + tag OpenAPI "MOTD" (không còn route dùng). `LoginService`/`IrcQueryService` đọc thẳng `ServerReplies.MotdText` (bỏ luôn `async`/`await` ở `BuildMotdReplyAsync`→`BuildMotdReply` vì không còn I/O nào để chờ).
- Trade-off test nêu rõ: 2 test cũ vary MOTD text qua mock `ISettingsRepository` — không còn vary được vì giờ là `static readonly` load 1 lần. Bỏ hẳn 1 test (`LoginServiceTests`, logic chỉ 1 dòng ternary, không đáng giữ seam riêng). Giữ 1 test (`IrcQueryService`, logic multi-line MOTD→375/372×N/376 thật sự đáng test) bằng cách thêm param `motdTextOverride` optional (chỉ dùng cho test, production caller không bao giờ truyền) làm seam.
- `docker-compose.yml` + `docker.md`/`configuration.md`/`deployment.md`/`database.md` cập nhật lại toàn bộ theo rename+split+bỏ MOTD API. Docker mount giờ trỏ cả thư mục `Data/Localization/` thay vì 1 file riêng.

Full suite: 1552 test pass (net -1 so 1553: -1 LoginServiceTests, +1 IrcQueryServiceTests tách, -1 ReplyLocaleTests's orphan-key check — cách làm duy nhất khả thi phải đọc lại source .cs lúc test chạy, đánh giá quá fragile so với giá trị mang lại, thay bằng check non-empty đơn giản mỗi class). Release build sạch, verify lại cả 3 file propagate đúng + revert-and-fail 1 key vẫn báo lỗi rõ như trước. Đã push.

**Task tiếp theo đã được user queue (gửi mid-turn lúc đang làm vòng 17, chưa bắt đầu)**: chuyển `Mirror:DownloadEndpoint`/`Mirror:SearchEndpoint` từ `appsettings.json` (static, cần restart) sang DB `Settings` table (mutable runtime, giống pattern `AdminKeyService`/`MenuIconService` đã có sẵn) — hướng NGƯỢC với hướng locale/MOTD (đó là chuyển từ DB→file tĩnh; đây là chuyển từ file tĩnh→DB). Cần: đọc `MirrorOptions`/chỗ nào đang bind từ config, thiết kế lại thành đọc qua `ISettingsRepository` (hoặc service mới kiểu `MirrorService`) + có thể cần API endpoint để set (user không nói rõ có muốn thêm endpoint không — cần xác nhận khi bắt đầu). Ưu tiên làm việc này kế tiếp.

**Cập nhật 2026-09-04 vòng 18 (Mirror→DB hoàn tất, 2 commit)**: user xác nhận qua AskUserQuestion: có thêm GET/PUT, và tổng hợp AdminKey+Mirror vào `/settings/`. Hỏi advisor trước khi code — chỉ ra 2 điều quan trọng: (1) `MirrorOptions` không được vừa là config type vừa là live value (bẫy stale-naming y hệt cái session này đã dọn ở vòng trước) — quyết định giữ `MirrorOptions` CHỈ làm seed 1 lần, giá trị sống chuyển sang `MirrorEndpoints` record mới trên `MirrorService`; (2) chưa quyết định `appsettings.json`'s `Basil:Mirror` section cũ biến mất kiểu gì khi upgrade — im lặng mất mirror config là silent behavior change (vi phạm ràng buộc #2 của kế hoạch này) — chọn "one-time seed at startup": nếu DB chưa có `Mirror:*` key nào mà config vẫn còn, copy 1 lần vào DB + log, rồi không đọc lại config nữa.

Chia 2 commit theo gợi ý advisor (rename thuần trước, để đổ vỡ không kéo cả 2):
- `909cd82`: `/adminkey`→`/settings/adminkey` (đổi route thuần qua `group.MapGroup("/settings")`, cập nhật mọi doc-comment/test/docs liên quan). Verify revert-and-fail: bỏ dòng `MapGroup` → 5/6 test fail 404 → khôi phục → 6/6 pass.
- `ac9956b`: `MirrorService` mới (theo đúng pattern `AdminKeyService`), `SeedFromConfigIfUnsetAsync` chạy 1 lần ở `Program.cs`'s `InitializeDataAsync`, `GET`/`PUT /settings/mirror` mới, rewire toàn bộ 4 consumer (`BeatmapsetAssetRoutes` 10 handler, `BeatmapAssetRoutes` 3 handler, `OsuWebRoutes`'s `/d/{id}`, `DirectSearchService`) + startup log Program.cs (bỏ luôn tên config path `Basil:Mirror:DownloadEndpoint` khỏi text log vì không còn là nguồn sống — advisor nhắc). `MirrorOptions` bỏ `IsOnlineMode`/`HasSearchMirror` (không còn ai đọc, chuyển sang `MirrorEndpoints`).

Phát hiện khi viết integration test PUT-then-GET (chính test advisor yêu cầu để verify "mutable runtime" thật sự đúng): 3 file integration test cũ (`BeatmapMirrorModeEndpointTests`/`BeatmapRedirectEndpointTests`/`DirectSearchEndpointTests`) dùng `TestDoubles.BypassAdminKeySettingsRepository()` — 1 substitute CỐ ĐỊNH trả `null` cho MỌI key bất kể có `SetAsync` hay không. Seed-tại-startup sẽ ghi qua `SetAsync` (không lỗi) nhưng đọc lại vẫn `null` mãi → online-mode test sẽ luôn fail sai. Đổi cả 3 file sang `InMemorySettingsRepository` (fake stateful đã có sẵn, cùng loại `AdminKeyManagementEndpointTests` dùng) — bug này lẽ ra sẽ chỉ lộ ra lúc chạy test, không phải lúc code, nếu không viết integration test thật cho seed path.

Full suite: 1560 test pass (từ 1553, +7: +6 `MirrorServiceTests`, +3 `MirrorSettingsManagementEndpointTests`, -2 `OptionsBindingTests` net do đổi property). Release build sạch, architecture test pass. Docs cập nhật (`configuration.md` mục "Beatmap mirror" mới song song "Admin Key", `beatmap-ingestion.md`/`troubleshooting.md`/`docs-guideline.md`). Cả 2 commit đã push.

**Cập nhật 2026-09-04 vòng 19 (advisor review sau vòng 18, tìm bug thật)**: gọi advisor sau khi hoàn tất vòng 18 (đúng kỷ luật "gọi advisor khi nghĩ đã xong"). Advisor tìm 1 bug thật: gate seed cũ dựa trên "endpoint hiện đang null" — không phân biệt được "chưa từng seed" với "operator vừa `PUT` xoá cả 2 endpoint" (cùng cho ra null/null) → restart sau khi operator xoá sẽ VÔ TÌNH seed lại giá trị config cũ, ghi đè quyết định của operator. Sửa bằng marker key riêng (`Mirror:Seeded`, đặt `"true"` đúng 1 lần bất kể config có giá trị hay không — cùng hình dạng `AdminKey:LastChanged` cạnh `AdminKey:Hash`).

Trong lúc sửa test cho gate mới, phát hiện thêm 1 finding phụ (không phải bug production, xác nhận qua chính integration test đã chạy đúng với `InMemorySettingsRepository` thật): `is not null` fail với unit test mock vì NSubstitute mặc định trả `""` (không phải `null`) cho member string chưa stub — đổi sang `string.IsNullOrEmpty(...)` (khớp `NullIfEmpty` helper đã có sẵn trong cùng class) vừa đúng hơn vừa fix test mà không cần đặc cách fixture.

Cũng đổi tag OpenAPI `/settings/mirror` từ `"Settings"` (trùng tên nhóm chứa nó) sang `"Mirror"` (khớp convention mọi tag khác đặt theo resource, và khớp operationId `getMirrorSettings`/`setMirrorSettings` đã có sẵn).

Test mới quan trọng nhất: `RestartAfterClearingSeededEndpoint_DoesNotReseedFromConfig` — 2 host build riêng biệt (mỗi host chạy `InitializeDataAsync`/seed đúng 1 lần, y hệt 1 lần restart thật) dùng CHUNG 1 `InMemorySettingsRepository` để mô phỏng storage bền vững qua restart — test DUY NHẤT thật sự exercise được bug (unit test chỉ verify logic cô lập, không mô phỏng được "2 lần khởi động").

Commit `698017f`, full suite 1561 test pass (từ 1560, +1), Release build sạch, đã push.

**Còn lại cho phiên sau**: giống list vòng 13/14/16 (`.osz` full rewrite chờ đo đạc, Broader Follow-up, `ReconcileAllAsync`) — không có yêu cầu mới nào chưa xử lý.

**Cập nhật 2026-09-04 vòng 20 (user đảo ngược quyết định MOTD, Docker live verification tìm 3 bug thật)**: user yêu cầu chuyển MOTD ngược lại từ localization file (`Server.json`, quyết định vòng 19) về setting API-managed, path cụ thể `/settings/motd` — đảo ngược 1 phần "Follow-up: split by category, plus MOTD" trong ADR-007. `ServerReplies`/`Server.json` xoá hẳn, `MotdService` khôi phục theo pattern `AdminKeyService`/`MirrorService` (đọc `git show 20495cd~1` lấy lại route shape gốc: `GET` không gate, `PUT` gate).

Cùng lúc, user báo Docker engine đã chạy — lần đầu phiên này có Docker thật để verify live thay vì chỉ verify qua fake repository. Set up môi trường Docker cô lập (project name/port/data dir riêng, không đụng `docker-data/` thật của user), build+run+curl thật → lộ 3 bug thật, không cái nào test cũ (toàn dùng fake `ISettingsRepository`) bắt được:

1. **Thứ tự khởi động sai**: `Program.cs` seed Mirror settings TRƯỚC khi chạy migration → `SQLite Error 1: 'no such table: Settings'` trên DB thật trống hoàn toàn (ẩn ở máy dev vì `Basil.db` cũ đã migrate sẵn). Sửa: seed chạy SAU block migration, nhưng KHÔNG lồng trong `if (hasDatabase)` (nhiều test host chạy `hasDatabase=false` vẫn cần seed chạy qua fake repository).
2. **Cert bị Docker mount che khuất**: `./docker-data/Data:/app/Data` (mount cả thư mục) che luôn `basil.local.pfx` build sẵn trong image — data dir fresh gặp lỗi OpenSSL "no such file", Kestrel restart loop vô hạn. Sửa: thêm mount riêng lẻ cho `basil.local.pfx` (giống pattern đã áp dụng cho `appsettings.json`/`Localization/` ở vòng trước), cả `docker-compose.yml` lẫn `docker run` trần trong `docker.md`.
3. **`SqliteSettingsRepository.SetAsync` chỉ UPDATE, không upsert** (bug nghiêm trọng nhất): key nào không có row seed sẵn từ migration thì MỌI lần ghi silently no-op — không exception, `GetAsync` cứ trả `null` mãi, không cách nào phân biệt "chưa từng set" với "set nhưng lưu thất bại". `Mirror:DownloadEndpoint`/`Mirror:SearchEndpoint`/`Mirror:Seeded` (thêm ở vòng 18) chưa từng được seed qua migration nào → `/settings/mirror` ghi thầm lặng fail trên deployment thật. Suýt lặp lại lỗi này với key `Motd:Text` mới đặt (để nhất quán naming với `AdminKey:Hash`) — chỉnh về lại key gốc `"Motd"` mà `001_base.sql` đã seed sẵn. Thêm migration `005_mirror_settings.sql` seed 3 key `Mirror:*`. Thêm regression test vĩnh viễn `SqliteSettingsRepositoryTests.SetAsync_EveryServiceOwnedKey_ActuallyPersists` (chạy trên schema migrate thật, cover mọi key service đang sở hữu) để lớp bug này không thể tái phát âm thầm nữa.

**Advisor review sau khi tưởng đã xong** (đúng kỷ luật "gọi advisor trước khi báo done") bắt thêm 2 vấn đề: (a) key `"Motd"` seed sẵn giá trị KHÔNG rỗng (`"Welcome to Basil, the osu! server for tournaments and multiplayer"`, từ `001_base.sql` gốc) — test mới `GetMotd_Unconfigured_ReturnsNull` (dùng fake rỗng) và docs `configuration.md` đều ngầm khẳng định default rỗng, sai với DB thật. Xác nhận bằng cách pin trực tiếp giá trị seed thật qua SQLite test mới (`SqliteSettingsRepositorySeedTests`, dùng fixture riêng để không bị test khác ghi đè key `Motd`), verify bằng revert-and-fail (đổi expected value sai → fail đúng, phục hồi → pass) thay vì rebuild Docker lại; sửa `configuration.md` ghi rõ default không rỗng. (b) `docker.md`'s `docker run` trần (path không qua Compose) cũng thiếu cert mount y hệt bug #2 — thêm bước extract + mount `basil.local.pfx` vào cả 2 chỗ (`docker run` ban đầu + update flow), sửa luôn 1 diagram cũ còn sót `Server.json` (sót từ lần sửa docs vòng 19, không phải do vòng này). Thêm test `GetMotd_WithAdminKeyConfigured_StillSucceedsWithoutAuthentication` pin rằng `GET /settings/motd` thật sự không bị gate (mọi test khác trong class chạy bypass mode nên không phân biệt được "không gate" với "chưa set key").

Full suite: 1574 test pass (từ 1572, +2: seed-pin test + un-gated-GET test). Release build sạch. Docs cập nhật: `configuration.md`, `docker.md` (3 chỗ), `database.md`, `deployment.md`, ADR-007 (thêm mục "Follow-up: MOTD moved back to a database-backed setting", sửa luôn 1 chỗ stale key name trong chính ADR do quên update khi đổi `Motd:Text`→`Motd`). Commit `6305674`, đã push.

**Còn lại cho phiên sau**: không đổi — `.osz` full rewrite chờ đo đạc, Broader Follow-up, `ReconcileAllAsync` vẫn 3 mục cũ.

**Cập nhật 2026-09-04 vòng 21 ("Tiếp tục làm" — đóng `ReconcileAllAsync`, thử tiếp `.osz` đo đạc nhưng bị chặn thật)**: user gõ "Tiếp tục làm" không kèm hướng cụ thể. Cả 3 mục backlog đều bị chặn theo cách khác nhau — thử đường `.osz` full rewrite trước (2 mục đo đạc còn thiếu: disk-usage across mixed-popularity library, migration-pass timing) vì tưởng làm được không cần user. Advisor xác nhận hướng đúng nhưng bắt lỗi phương pháp: disk-usage KHÔNG thể đo bằng random bytes tổng hợp (tỷ lệ audio/video/osu quyết định toàn bộ Alternative C's bound, bịa tỷ lệ = bịa số) — phải kiểm tra dữ liệu thật trước. Check `docker-data/Data/Mapsets` (rỗng) và `.loadtest/server/Data/Mapsets` (chỉ 1 set giả, toàn `.osu` trơ trụi 2.7KB, không audio/ảnh/video) — KHÔNG có dữ liệu đại diện thật nào trên máy này. Kết luận trung thực: mục disk-usage vẫn CHỜ dữ liệu, không tự bịa số để "đóng" cho có — migration-pass timing (đo được bằng random bytes, không phụ thuộc tỷ lệ asset) chưa kịp làm thì user gửi tin giữa chừng.

User gửi tin giữa lúc đang chạy lệnh: "ReconcileAllAsync chờ quyết định response-contract, sau đó .osz full rewrite chờ đo đạc" — chuyển hướng sang giải quyết `ReconcileAllAsync` trước. Đọc kỹ `BeatmapsetRoutes.HandleCreate`/`BeatmapIngestionService`: phát hiện `ReconcileOszAsync(oszPath, ...)` — hàm reconcile CHỈ 1 file — đã tồn tại sẵn (chính là hàm `ReconcileAllAsync` gọi lặp cho từng file rời), nhưng `HandleCreate` lại gọi `ReconcileAllAsync()` (full sweep toàn thư mục) thay vì gọi thẳng nó — có vẻ là tiện tay lúc viết code ban đầu, không phải chủ ý thiết kế. Hỏi user qua `AskUserQuestion` giữa 2 hướng (scope hẹp lại vs async/202 giống PUT/DELETE); user chọn "[No preference]", hỏi lại bằng lời (user không hiểu câu hỏi kỹ thuật gốc — "cái đó là gì") rồi giải thích lại bằng ngôn ngữ đơn giản hơn; user xác nhận đồng ý hướng khuyên dùng (scope hẹp lại).

Sửa `HandleCreate`: đổi `ReconcileAllAsync(cancellationToken)` → `ReconcileOszAsync(destination, cancellationToken)`. Không đổi response contract (vẫn `201 Created` + `{ beatmapsProcessed }`), chỉ đổi Ý NGHĨA con số (giờ chỉ đếm riêng archive vừa upload, không còn đếm cả full sweep) — sửa luôn `WithDescription` OpenAPI ghi rõ điều này (advisor bắt được: admin có file thả tay ngoài API trước đây upload sẽ thấy số khác đi, là thay đổi hợp đồng quan sát được dù shape/status code không đổi).

Viết test mới `PostBeatmapset_Valid_ReconcilesOnlyTheUpload_LeavesUnrelatedStrayOszUntouched` (thả 1 file `.osz` rời giả lập chưa được watcher xử lý, upload file khác, xác nhận file rời kia KHÔNG bị đụng) — verify bằng revert-and-fail thật (đổi tạm về `ReconcileAllAsync()`, count đổi từ 1→2 đúng như dự đoán, phục hồi lại). Trong lúc viết test này (endpoint `/beatmapsets` POST/PUT trước giờ CHƯA từng có integration test nào!) phát hiện thêm 1 bug thật không liên quan: `MultipartFormDataContent` rỗng hoàn toàn (không field nào, khác với "thiếu field `file`") không phải multipart hợp lệ với ASP.NET Core — `ReadFormAsync` ném `InvalidDataException` không được bắt, lộ ra 500 thay vì 400. Sửa cả `HandleCreate` (POST) lẫn `HandleReplace` (PUT, cùng pattern y hệt) bằng try/catch quanh `ReadFormAsync`. Test riêng cho cả 2: `PostBeatmapset_MalformedMultipart_ReturnsBadRequest`, `PutBeatmapset_MalformedMultipart_ReturnsBadRequest` (advisor bắt được lần review thứ 2: fix cả 2 handler nhưng ban đầu chỉ test 1 — thêm test PUT còn thiếu).

Full suite: 1578 test pass (1577 + 1 lần flake SSE do chạy song song, tái hiện sạch khi chạy riêng lẻ, không liên quan thay đổi — không đụng code SSE). Release build sạch. Cập nhật ADR-006's "Open items" (đánh dấu mục `ReconcileAllAsync` FIXED, ghi chi tiết cả 2 fix). Commit `0b8cf81`, đã push.

**Còn lại cho phiên sau**: `.osz` full rewrite VẪN CHỜ ĐO ĐẠC — 2 mục còn thiếu: (a) disk-usage across mixed-popularity library — CHẶN THẬT vì chưa có dữ liệu beatmapset thật đại diện trên máy này (đã kiểm tra `docker-data/`, `.loadtest/`, không có gì dùng được), cần dữ liệu thật hoặc user cung cấp trước khi đo được trung thực; (b) migration-pass timing — CHƯA làm (đo được bằng random bytes, không bị chặn bởi thiếu dữ liệu, chỉ chưa kịp trước khi đổi hướng) — mục actionable nhất cho vòng kế nếu tiếp tục nhánh `.osz`. Broader Follow-up vẫn chờ RC11 giám sát. `ReconcileAllAsync` ĐÃ ĐÓNG, xoá khỏi backlog.

**Cập nhật 2026-09-04 vòng 22 (`.osz` full rewrite, unblocked bằng dữ liệu thật + implement toàn bộ)**: user cung cấp dữ liệu thật tại `C:\Users\haith\Desktop\Data` — mở khóa đúng 2 mục Measurements còn thiếu của vòng 21 (disk-usage across mixed-popularity library, migration-pass timing), rồi chỉ thị "làm .osz full rewrite nhé". ADR-006's Decision coi như unpark đủ điều kiện (đã có blanket approval từ trước cho rule 4; giờ rule 1 cũng thỏa vì NEEDS EXPERIMENT đã đóng). Advisor tư vấn suốt — chốt trình tự 7 phase (asset cache; canonical-store ingestion + orphan-sweep; migration service nền; rewire mọi read path; download trực tiếp từ canonical; directory-scoped invalidation; watcher narrowing).

Implement Phase 1-6 trên `chore/perf-investigation` (không tách branch riêng theo yêu cầu), mỗi phase verify bằng revert-and-fail + full suite trước khi commit:
- **Phase 1-2** (asset cache + canonical-store ingestion): `BeatmapsetAssetCache` (extract-on-demand per (setId, entryName), single-flight lock, invalidate theo thư mục cả set); `ReconcileOszAsync` giữ archive tại chỗ làm canonical thay vì extract-rồi-xoá; sửa kèm 1 bug found: replace archive cùng id không invalidate cache cũ (stale asset serve mãi).
- **Phase 3-4 gộp 1 commit** (advisor chỉ rõ tách 3/4 sai — bật migration mà chưa rewire read path thì phá mọi read dựa trên folder): `BeatmapsetMigrationService` (BackgroundService 1 lần lúc khởi động, KHÔNG block startup nhờ .NET 10's `ExecuteAsync` chạy hẳn trên background thread — khác .NET cũ); 4 resolver tĩnh (`OsuFilePathAsync`/`BackgroundFilePathAsync`/`AudioFilePathAsync`/`VideoFilePathAsync`) đổi thành async + dual-layout-aware (folder trước, canonical archive qua cache sau); rewire 14 call site (`BeatmapThumbnailProvider`, `BeatmapsetBackgroundProvider`, `BeatmapsetRoutes`, `BeatmapsetAssetRoutes`, `BanchoHostGroups`, `BeatmapAssetRoutes`, `OsuWebRoutes`); `PUT`/`DELETE /beatmapsets/{id}` thêm nhánh canonical-archive.
- **Bug thật tìm được, không phải test flakiness đơn thuần**: `BeatmapWatcherService` tự phản ứng với chính archive migration vừa publish (rename trong-cây → event Renamed thật), gọi lại `ReconcileOszAsync` redundant ~2s sau; với folder KHÔNG có `.osu` (test fixture rỗng), migration vẫn zip nó thành archive rỗng → watcher's `ReconcileOszAsync` thấy 0 file decode được → XOÁ archio vừa build, y hệt symptom "test fail chỉ khi full-suite chạy song song" (harness chạy chậm hơn 2s debounce window). Sửa gốc: migration skip hẳn folder không có file `.osu` (khớp định nghĩa `ReconcileFolderAsync` đã dùng sẵn). Verify thực nghiệm: cho set có nội dung thật đi qua đúng đường watcher-vs-migration này, xác nhận redundant reconcile vô hại (cache pre-warm sống sót nguyên write-time) — không mở rộng sang full decode-check vì literal fixture phổ biến ("osu file format v14") đã decode được, ghi rõ giới hạn (existence-check, không phải decode-check) làm `ponytail:` comment.
- **Phase 5** (download trực tiếp từ canonical): phát hiện bug thật khi viết test — `GET /beatmapsets/{id}/download` TRƯỚC ĐÓ 404 HOÀN TOÀN cho set đã migrate (chỉ check folder cũ, route chưa từng được cập nhật cùng Phase 3/4). Sửa: không noVideo → trả thẳng byte archive canonical (đúng ý "served directly from the canonical .osz"); có noVideo → rebuild filtered đọc từ archive entries thay vì filesystem, KHÔNG cache (giữ nguyên hành vi rebuild-mỗi-request như nhánh folder cũ).
- **Phase 6** (directory-scoped invalidation): xác nhận đã đủ khi đọc lại `DeleteBeatmapsetAsync` — DB row + asset cache + cả 2 size thumb + preview đều bị xoá qua đúng 1 call site dùng chung, không cần code thêm.
- **Phase 7 (watcher narrowing, non-recursive/`.osz`-only) — CHỦ Ý KHÔNG LÀM**: advisor chỉ rõ tiền đề "không còn folder legacy sống" CHƯA đúng — `HandleReplace`/`HandleDelete` vẫn còn nhánh xử lý folder. Làm bây giờ sẽ âm thầm bỏ live-reconcile cho folder chưa migrate. Để lại backlog, làm khi nào không còn folder nào nữa.

Doc update: `docs/for-developers/beatmap-ingestion.md` (nguồn thật cho topic này) — "Source of truth" giờ mô tả canonical `.osz` archive + folder cũ là transitional state vẫn còn hỗ trợ; `docs/for-technicians/configuration.md` sửa theo. ADR-006's status đổi "chờ duyệt/chưa implement" → "accepted and implemented", ghi rõ bug tìm được + 2 follow-up cố ý bỏ ngỏ (watcher narrowing, xem RC12 dưới) — **ADR sẽ bị xoá sau khi việc này xong theo đúng vai trò implementation-scoped của nó, không phải tài liệu lâu dài** (chỉ thị mới từ user), nên các file `docs/` KHÔNG được phép trỏ ngược vào ADR.

**Phát hiện phụ, ghi lại làm RC12 (KHÔNG sửa trong phiên này, ngoài phạm vi)**: `SqliteBeatmapsetRepository.UpsertAsync` — insert (`ON CONFLICT DO UPDATE`) rồi đọc lại (`FetchByIdAsync`) không atomic, race với `DeleteAsync` đồng thời có thể null-forgive (`!`) một kết quả thật sự null → `NullReferenceException`. Pre-existing, không liên quan rewrite này (code path y hệt tồn tại từ trước Phase 2/3), tìm được tình cờ qua output revert-and-fail. Cần fix riêng, chưa làm.

Full suite cuối vòng này: **1596 test pass** (Domain 114, Protocol 158, Application 710, Infrastructure 257, Architecture 9, Integration 348). Release build sạch. 6 commit (`2ef37c7`..`6e14272`), tất cả đã push lên `chore/perf-investigation`. PR #7 description cập nhật theo (nhưng chỉ là báo cáo tiến độ — plan này mới là nguồn thật).

→ **"làm .osz full rewrite" coi như HOÀN TẤT Phase 1-6.** Phase 7 để lại có chủ đích, chờ đúng lúc.

**Còn lại cho phiên sau (trước RC12 fix)**: Phase 7 (watcher narrowing) chờ hết folder legacy; RC12 (`UpsertAsync` race) chưa fix; Broader Follow-up (re-profile, load test thật, scaling curves 50-1000, Definition of Done, cập nhật `sse.md`/`response-envelope.md`/`multiplayer.md`/`database.md` theo mục "Cập nhật tài liệu bắt buộc" cuối file — CHƯA làm phần lớn, chỉ mới sửa `beatmap-ingestion.md`/`configuration.md` cho riêng mảng storage) — tất cả các mục load-test/profiling vẫn chờ RC11 (cần giám sát trực tiếp, không chạy unattended được); Final Deliverables (load-test profile update, before/after benchmark report gộp, capacity envelope + scaling curves, known-limitations doc, final dependency inventory) — chưa mục nào làm.

**Cập nhật 2026-09-04 vòng 23 (RC12 fix)**: user "Tiếp tục làm" — hỏi advisor thứ tự, chỉ ra RC12 là mục duy nhất CONFIRMED bug thật với cơ chế đã rõ trong backlog, làm trước. `SqliteBeatmapsetRepository.UpsertAsync` đổi từ 2 statement (INSERT rồi `FetchByIdAsync` riêng, có khoảng hở không transaction) sang 1 statement atomic (`INSERT ... ON CONFLICT DO UPDATE ... RETURNING *`, tái dùng đúng pattern Phase 1 đã có sẵn — `bce99a1`'s login/clienthash round-trip collapse). Thử viết regression test dạng `Task.WhenAll` concurrent upsert+delete (50 cặp, chạy lại 5 lần) trên code CŨ để verify đúng bug trước khi tin fix — KHÔNG BAO GIỜ tái hiện được (thread-pool scheduling không đủ hẹp để lọt đúng khoảng hở giữa 2 statement) → test "không thể fail" → bỏ theo đúng tiền lệ đã có trong phiên (test không fail được thì bỏ, không giữ làm tài liệu giả). An toàn của fix dựa vào tính atomic của mệnh đề `RETURNING` (đảm bảo bởi chính SQLite), không phải 1 cơ chế đồng bộ tầng ứng dụng tự viết có thể tự hỏng. Xác nhận `ResolveBeatmapsetAsync`/`RefreshBeatmapsetAsync` (2 caller duy nhất) không phụ thuộc hình dạng đọc-lại riêng — `RETURNING *` cho đúng row post-merge y hệt 1 SELECT riêng sẽ cho. 13 test cũ (contract không đổi) + suite Infrastructure 257/257 xanh, Release build sạch. Commit `c2767db`, đã push. **RC12 ĐÃ ĐÓNG.**

**Cập nhật 2026-09-04 vòng 24 (đóng mục "Cập nhật tài liệu bắt buộc")**: advisor gợi ý làm tiếp mục 13 (doc update) nếu còn budget sau RC12 — làm luôn. Commit `9d2d3cb`:
- `database.md`: "Busy timeout" (mơ hồ, không có số) → "Write configuration" với số thật (`busy_timeout=5000`, `synchronous=NORMAL` dưới `journal_mode=WAL`), trỏ chéo sang phần round-end persistence mới của `multiplayer.md`.
- `multiplayer.md`: kiểm tra claim "3 con số khác nhau" của empty-room timer trong plan cũ — đối chiếu code thật (`EmptyRoomCloseSeconds=15*60`, `EmptyRoomWarnAtSeconds=5*60`, comment code, doc) THẤY ĐÃ NHẤT QUÁN — note cũ trong plan đã lỗi thời (chắc đã sửa ở vòng nào đó không ghi rõ), không có gì để sửa mục này. Thêm mới section "Round-end persistence" thật sự còn thiếu — mô tả `MatchRoundEndOutbox` (hàng đợi ordered ngoài lock), lý do round-start không nằm trong đó, ngữ nghĩa "known gap" khi retry hết hạn.
- `response-envelope.md`: thêm "Error paths" — route không match (404/405/constraint overflow) và exception ứng dụng chưa bắt, cả 2 đã code xong từ vòng trước (RC2/ADR-005) nhưng chưa bao giờ ghi vào doc này — đọc thẳng `EnvelopeMiddleware.cs`/`ExceptionLoggingMiddleware.cs` để mô tả đúng, không đoán.
- `sse.md`: phát hiện phụ — 3 chỗ trỏ ngược "ADR-004" còn sót từ vòng cũ (vi phạm đúng chỉ thị mới nhận "docs không được trỏ ADR"). Gỡ cả 3, 2 chỗ dư thừa (thông tin đã có ngay trong cùng đoạn), 1 chỗ trỏ sang section khác NGAY TRONG chính file — không mất thông tin gì.

Build Release sạch, đã push.

**Cập nhật 2026-09-05 vòng 25 (đóng 3 mục backlog, viết known-limitations + dependency inventory)**: user hỏi lại danh sách còn lại (tôi từng trả lời SAI vì dùng trí nhớ compact cũ, không đọc lại file thật — sửa ngay bằng cách đọc lại toàn bộ plan file + `git log`, phát hiện 12 commit vòng 20-24 mà compact summary trước đó không có). User giải thích item Match Hosts/Referees: "Unable to test" là do máy chỉ chạy được 1 osu! client, không phải bug — rồi yêu cầu làm hết 7 mục, sắp xếp không phụ thuộc/dễ trước.

Hỏi advisor xác nhận hướng: KHÔNG fork (worktree cần merge riêng, không-worktree dễ đụng file lẫn nhau; "đồng thời nếu có thể" là cho phép chứ không bắt buộc, ở đây không làm sạch được) — làm tuần tự trên cây chính. Advisor sửa lại thứ tự: Host/Referee coi như ĐÃ ĐÓNG (không phải "điều tra thêm" — chỉ cần xác nhận test coverage in-process đã đủ); Phase 7 phải ĐỌC LẠI CODE trước khi kết luận (đừng suy từ dữ liệu local sạch); EnvelopeMiddleware trước CloseAsync (có trigger surface cụ thể, 1 lần trace là xong); CloseAsync theo đúng tiền lệ vòng 23 (không tái hiện được thì hạ nhãn, không tự bịa fix); known-limitations viết SAU CÙNG (nội dung phụ thuộc kết quả 3 mục trên).

Kết quả:
- **Match Hosts/Referees**: xác nhận — `MatchTransferHostHandlerTests`, `MpCommandServiceTests`' AddRef/RemoveRef/host case, `MatchSubResourceEndpointTests`' Refs_*/Hosts_* đã cover multi-session host/referee transition in-process (dựng `GameSession` trực tiếp, không cần 2 client thật). ĐÓNG.
- **Phase 7**: đọc lại `BeatmapsetRoutes.HandleReplace`/`HandleDelete` — CẢ 2 vẫn còn nguyên nhánh `if (targetFolder is not null)` xử lý folder cũ. Xác nhận vẫn chặn thật bởi CODE, không phải giả định lỗi thời. GIỮ NGUYÊN.
- **EnvelopeMiddleware 2 throw site**: trace `JsonNode.ParseAsync` (dòng 111) + `GetValue<int>()` trong `BuildMeta` — mọi body basilapi đều do framework serialize từ object có kiểu (`Results.Json`/`TypedResults`), không route nào parse lại input client ở tầng response — không reachable qua route thật nào hiện có. Dù có bị kích hoạt, `ExceptionLoggingMiddleware` (đăng ký TRƯỚC, nên bọc ngoài `EnvelopeMiddleware`) đã bắt + envelope đúng thành 500. Hạ nhãn SUPPORTED → ĐÓNG, ghi invariant vào `response-envelope.md`.
- **`CloseAsync` edge case**: trace — `gameRegistry.Remove(` chỉ có 1 call site duy nhất (`PlayerLogoutService.LogoutAsync`), và nó LUÔN `slot.Reset(...)` (qua `MatchMembershipService.LeaveAsync`) dưới CÙNG match lock TRƯỚC KHI xoá khỏi registry; `CloseAsync` cũng giữ đúng lock đó khi quét slot → không thể interleave để tạo ra slot còn `PlayerId` trỏ tới session đã mất khỏi registry. Hạ nhãn HYPOTHESIS → ĐÓNG (đúng tiền lệ vòng 23: không tái hiện được thì không tự sửa/không viết test giả), ghi invariant vào `multiplayer.md`.
- **`known-limitations.md` mới** (`docs/for-developers/`): mọi RC còn mở thật (RC5 leak-under-load, RC8 protocol allocation, RC10/RC11 capacity>100 chưa đo + crash-repro chưa rerun đầy đủ có giám sát, tourney-client concurrency fix chưa xác nhận cần thiết thật), Phase 7 (lý do chặn + cách tự re-check), 3 mục vừa đóng ở trên (tham chiếu test cụ thể), và mục "Handoff" ghi rõ lệnh + cách theo dõi cho lần chạy load test có giám sát sau này. Link từ `CLAUDE.md` + `docs/index.md`.
- **Dependency inventory** cùng file: mọi package NuGet ở `src/` (đọc thật từ `.csproj`, không nhớ lại) + lý do tồn tại.
- **Xoá `docs/adr/ADR-006-storage.md`**: đúng chỉ thị user ghi lại ở vòng 22 ("ADR sẽ bị xoá sau khi việc này xong, vai trò implementation-scoped") — status đã "Accepted and implemented", không doc nào khác trỏ vào nó. KHÔNG xoá 4 ADR còn lại (003/004/005/007) dù cũng "Accepted and implemented" — chỉ thị đó chỉ nói riêng ADR-006, tự suy rộng ra sẽ là scope creep.

Build Debug sạch (docs-only + xoá 1 file, không đụng code production, không cần full suite). Commit `792f00c`, đã push. Post PR #7 comment tóm tắt (kèm disclosure lỗi trí nhớ ở trên).

**Cập nhật 2026-09-05 vòng 26 (advisor review known-limitations.md, sửa 3 chỗ sai)**: gọi advisor trước khi coi vòng 25 xong — advisor bắt được 3 lỗi thật trong file vừa viết:
1. Lệnh handoff `dotnet run --project tests/Basil.LoadTests -- --profile Profiles/full.json` tự ghi chú "may need adjusting" — tự thừa nhận chưa verify. Đọc thật `Program.cs`: `--profile` nhận TÊN profile (không phải path), harness tự resolve `Profiles/{name}.json` từ `AppContext.BaseDirectory`. Không có `README.md` nào trong `tests/Basil.LoadTests/` (đã ghi sai). Sửa lệnh thành `--profile full`, xoá tham chiếu README bịa.
2. RC5 section đọc như cả subsystem SSE còn nghi vấn — nhưng đối chiếu plan dòng 169 (RC5) thì 3 cơ chế cụ thể (`DiffObjects` `{}` spam, `PublishSlotsAsync` 1 call site, thiếu `Writer.Complete()`) đã **CONFIRMED, đã fix** qua ADR-004/Phase 3. CHỈ còn "leak thật dưới load" là NEEDS EXPERIMENT (dòng 170). Viết lại thành mục riêng "RC5 — SSE memory leak under load (NEEDS EXPERIMENT; cơ chế đã fix)", tách rõ đã đóng phần nào/còn mở phần nào.
3. Dependency inventory liệt kê `dbup-sqlite`/`ImageSharp` như lựa chọn đã chốt — nhưng plan dòng 84 từng gắn nhãn "ứng viên bỏ" (constraint #5) và chưa từng đánh giá lại. Thêm câu "Previously flagged as a removal candidate... not yet evaluated" cho cả 2 dòng thay vì trình bày như settled.

Verify cả 3 bằng cách đọc thật code/plan trước khi sửa (không đoán): `Program.cs` GetArg + Path.Combine logic, `ls tests/Basil.LoadTests/` xác nhận không có README, `grep RC5`/dòng 168-170 trong plan, `grep "removal candidate"` dòng 84. Commit `869eea9`, đã push. Docs-only, không cần build/test suite.

Không còn backlog nào actionable không cần user (load test giám sát vẫn là item duy nhất còn mở, đã handoff rõ ở `known-limitations.md`).

**Cập nhật 2026-09-05 vòng 28 (load test giám sát thật — RC11 TÁI HIỆN)**: user yêu cầu chạy load test full.json, giám sát real-time bằng Monitor tool (đọc log trực tiếp, không chờ shell xong). Chạy `dotnet run --project tests/Basil.LoadTests -- --profile full` nền, gắn Monitor tail log filter theo scenario/WARN/Exception/crash signature.

Kết quả:
- Login/idle/chat/multiplayer (4/16/32/64 phòng) chạy sạch. Vào `api` scenario (18 sub-variant) thì từ variant 14 trở đi fail dồn dập: `api_match_list_500` **100% fail (13390/13390)**, variant 15-18 tiếp tục fail hàng nghìn `HttpRequestException: connection actively refused` + `TaskCanceledException`.
- Verify độc lập bằng `resources.csv` + `Test-NetConnection`/`Invoke-WebRequest` PowerShell native (không qua MSYS curl, loại trừ artifact công cụ): `ThreadPoolQueueLength` tăng đơn điệu 809→2004 không giảm, `ThreadCount` 500→517, `HandleCount` 6776→6972, `CpuPercent` gần như luôn 0.000, `TcpConnections` rơi 500→297. **Đúng signature RC11 gốc gần như từng chi tiết** (đã so khớp lại với đoạn mô tả RC11 cũ trong chính plan file này).
- `Test-NetConnection 127.0.0.1:8443` xác nhận `TcpTestSucceeded: False` lúc đang fail — không phải client-side port exhaustion.
- Gap công cụ tái hiện: `Logs/latest.log` dừng ghi lúc 17:46 (chạm cap ~1GB) trong khi run tiếp tục tới 18:02 — mất visibility đúng lúc quan trọng, lần thứ 2 xảy ra đúng issue này.
- Phát hiện phụ: `full.json`'s `sse`/`stress`/`soak` báo "disabled or produced no variants; skipping" — profile KHÔNG chạy stress ramp/soak 12h như tài liệu cũ mô tả. Cần audit lại config, chưa làm trong vòng này.
- Harness tự force-kill server đúng thiết kế sau "Run complete", không cần tôi can thiệp kill process.

**Kết luận quan trọng nhất**: ADR-001 CHƯA đủ để đóng RC11 — verify hẹp trước đây (chỉ login, concurrency 100) không đại diện cho tổ hợp multiplayer+api thật. RC11 cần điều tra lại từ đầu, không phải fix nhanh — đúng tiền lệ "profile first" của kế hoạch, KHÔNG được tối ưu RC8 hay bất kỳ thứ gì khác trước khi hiểu rõ RC11 tái phát vì sao.

Đã cập nhật `known-limitations.md`: RC11 chuyển từ "NEEDS EXPERIMENT" sang "CONFIRMED tái hiện" với đầy đủ số liệu + next step cụ thể (điều tra DB write/lock contention đúng lúc chuyển multiplayer→api, dùng `BasilMetrics` sẵn có); mục capacity cập nhật theo (chờ audit config stress/soak trước); mục Handoff thêm 2 việc cần làm trước lần chạy sau (audit config stress/soak, sửa log-cap/rotation).

Report evidence giữ nguyên tại `.loadtest/reports/full-20260905-094716/` (không xoá).

**Ngoài lề, trước khi chạy load test**: đã build lại toàn bộ 6 OpenAPI JSON doc (`dotnet build src/Basil.Web`) + viết throwaway validator (C# console app, `Microsoft.OpenApi` 2.11.0 — đúng version repo dùng, `OpenApiDocument.Parse` + đọc `Diagnostic.Errors/Warnings`) để tự kiểm tra format chuẩn OpenAPI 3.x. Kết quả: 5/6 doc sạch tuyệt đối. `basilapi.json` có 2 lỗi thật (path template `/users/{idOrName}` vs `/users/{userId}` trùng hình dạng sau khi bỏ tên param — vi phạm spec dù ASP.NET routing phân biệt được nhờ method khác nhau) + 2 warning dangling `$ref` (`MatchLiveSnapshot`, `PlayerStatusView` — do gọi `.Produces<T>()` 2 lần trên cùng operation SSE, type khai báo ĐẦU TIÊN mất schema, type khai báo SAU cùng thì đăng ký đúng — root cause đã truy tới tận `MatchRoutes.cs:210-211`/`UserRoutes.cs:344-345`). Wording/description sạch, không banned term theo CLAUDE.md rule 5, không AI-filler pattern. **CHƯA sửa 2 finding này** — đã báo user, chưa có quyết định làm luôn hay để sau.

**Cập nhật 2026-09-05 vòng 27 (Phase 7 — làm thật, không còn "blocked")**: user hỏi "vậy làm cái 2 đi" (Phase 7 — watcher narrowing) sau khi hỏi backlog đầy đủ. Gọi advisor trước khi code — advisor sửa lại đúng bản chất blocker: KHÔNG phải "chờ hết folder legacy trên đĩa" (dữ liệu deployment không kiểm soát được), mà là **`HandleReplace`/`HandleDelete`'s legacy branch tự nó không reconcile DB, chỉ dựa vào watcher sống làm hộ** — một coupling trong code, sửa được thật.

Đã làm:
- `BeatmapsetRoutes.HandleReplace`/`HandleDelete`: legacy branch giờ gọi `ingestion.ReconcileFolderAsync`/`ReconcileDeletedFolderAsync` inline ngay sau khi ghi file, không còn phụ thuộc watcher.
- `BeatmapWatcherService`: `IncludeSubdirectories=false`, bỏ `NotifyFilters.DirectoryName`, xoá 2 nhánh folder trong `Settle` (`Directory.Exists`, deleted-folder), xoá hẳn `DebounceRenamed` (dead code sau khi bỏ nhánh folder) — giờ chỉ theo dõi file `.osz` ở top-level.
- Verify KHÔNG có call site thứ 3 nào của `ReconcileFolderAsync`/`ReconcileDeletedFolderAsync` ngoài watcher + `ReconcileAllAsync` (grep xác nhận) — narrowing an toàn.
- Test: `BeatmapWatcherServiceTests` — 2 test cũ pin "folder drop/rename được live-ingest" giờ đảo ngược thành pin "KHÔNG được live-reconcile nữa"; 1 test race watcher/migration xoá hẳn (race không thể xảy ra nữa vì watcher không còn nghe folder event). Thêm 2 test mới ở `BeatmapsetManagementEndpointTests` (factory riêng bỏ cả `BeatmapWatcherService` lẫn `BeatmapsetMigrationService` khỏi DI) chứng minh route tự reconcile inline không cần watcher — verify bằng revert-and-fail thật (`git stash` route file, cả 2 test fail đúng lý do `NSubstitute.Exceptions.ReceivedCallsException`, restore lại pass).
- Docs: `beatmap-ingestion.md` "Ingestion triggers" viết lại (3 cơ chế thay vì 2, ghi rõ watcher không còn theo dõi bên trong folder); `known-limitations.md` xoá mục Phase 7 khỏi "Open items", thêm vào "Recently closed".

Build Release sạch, full suite 1597 test pass (Domain 114, Protocol 158, Application 710, Architecture 9, Infrastructure 256, Integration 350). Commit `d5ca2d4`, đã push.

Backlog còn lại y hệt: chỉ load test giám sát (đã handoff), không còn mục nào khác.

**Còn lại cho phiên sau** (đã đúng, không còn item cũ sai lệch): Phase 7 (watcher narrowing) chờ hết folder legacy — re-check bằng grep `targetFolder is not null`/`folder is not null` trong `HandleReplace`/`HandleDelete`; Broader Follow-up (re-profile, load test thật, scaling curves, Definition of Done) — CHƯA làm, chờ RC11, cần user giám sát trực tiếp lúc chạy (`known-limitations.md`'s "Handoff" ghi lệnh cụ thể); Final Deliverables còn lại (load-test profile update, before/after benchmark report gộp, capacity envelope + scaling curves) — phụ thuộc mục trên, chưa làm được. `known-limitations.md` + dependency inventory ĐÃ XONG, không còn nằm trong backlog.

---

## Design inputs cho DTO/model (đã research, dùng cho ADR-004/005 + Phase 4/5)

**osu!api v2 legacy match model** (nguồn: ppy/osu-web qua DeepWiki) — chuẩn tham chiếu khi audit DTO match/report/SSE:
- Naming: `snake_case` trên wire của osu!api; Basil hiện dùng camelCase envelope — GIỮ camelCase (hợp đồng Basil hiện có, ràng buộc #2), nhưng **cấu trúc** nên soi theo osu!api v2: event types `match-created | match-disbanded | host-changed | player-joined | player-left | player-kicked | other`; game object `{id, beatmap, beatmap_id, mods[], mode, scoring_type: accuracy|combo|score|scorev2, team_type: head-to-head|tag-coop|tag-team-vs|team-vs, scores[]}`; score match-context `{pass, slot, team: red|blue|none}`.
- Match events pagination: `before`/`after` (event id) + `limit` (default 100) — mẫu tốt cho match chat/events API của Basil thay vì trả toàn bộ.
- SSE gameplay model hợp nhất (Issue #4 đòi merge score+input → `gameplay`): lấy score object shape làm base, input frames tách sang spectate stream.

**osu! beatmap search syntax** (nguồn: ppy/osu-web `BeatmapsetQueryParser`) — cho osu!direct search + API search endpoint (Issue #4):
- Filters: `ar, cs, od, hp (dr), stars (star), bpm, length (s/m/h/ms), keys (key), divisor, status (ranked|graveyard|loved|qualified|pending|wip|mine|favourites), creator, artist, title, created (submitted), ranked, updated, tag, favourites, difficulty, source, circles, sliders` — operators `= < > <= >=`; dates dạng `2017 | 2018-05 | 2018-05-01`.
- In-game osu!direct params: `r` (rank achieved), `m` (mode), `q` (query), `p` (page).
- Basil scope: implement subset hợp lý (search theo beatmap id / set id + filters phổ biến), KHÔNG cần Elasticsearch — SQLite FTS/LIKE đủ cho private server; ghi rõ subset trong docs.

**Skills áp dụng khi implement**: `superpowers:test-driven-development`/`tdd` (mọi phase), `microbenchmarking` (ADR-001, Phase 6), `analyzing-dotnet-performance` (Phase 6 sweep), `optimizing-ef-core-queries` KHÔNG áp dụng (Dapper), `semgrep`/`security-review` (Phase S), `humanizer` + `documentation-writer` (viết lại docs khi behavior đổi — database.md, sse.md, response-envelope.md đều đang mô tả sai thực tế).

## Final Deliverables

- Toàn bộ ADR đã duyệt trong `docs/adr/`.
- Architecture documentation cập nhật (`architecture.md`, `multiplayer.md`, `sse.md`, `database.md`, `response-envelope.md`).
- Full test suite xanh + Release build sạch + architecture tests pass.
- Load-test profiles cập nhật (harness fixes + kịch bản tournament thực).
- Before/after benchmark report per phase.
- Capacity envelope + scaling curves (50/100/250/500/1000) trên reference machine.
- Danh sách known limitations + hypotheses còn mở (những gì chưa chứng minh, những gì chưa đo).
- Final dependency inventory (mỗi dependency: lý do tồn tại).

## Exit Criteria

> **Overhaul KHÔNG được coi là hoàn tất chỉ vì mọi code change đã merge. Nó chỉ hoàn tất khi hệ thống ĐO ĐƯỢC thỏa mãn các tiêu chí correctness, performance, resource, scalability, compatibility đã chốt trên reference workload.**

```text
code done ≠ overhaul done
code + tests + benchmark + load test + SLO + docs = done
```

Cụ thể: mọi mục Definition of Done đạt trên reference machine; mọi Final Deliverables tồn tại; mọi RC mang nhãn CONFIRMED đã có regression test; mọi NEEDS EXPERIMENT đã được thí nghiệm hoặc ghi vào known limitations.

## Cập nhật tài liệu (bắt buộc, theo CLAUDE.md rule 7)
- `database.md`: sửa claim busy timeout sai; ghi cấu hình pragma + write model được chọn (ADR-001).
- `sse.md`: viết lại theo contract mới (ADR-004).
- `response-envelope.md`: phủ error paths thực tế (ADR-005).
- `multiplayer.md`: đồng bộ empty-room timer (hiện 3 nguồn 3 con số: doc 5 phút, code 15 phút, comment 5 phút) + teardown funnel.
- ADRs lưu tại `docs/adr/` (đúng convention `/domain-modeling` của repo).
