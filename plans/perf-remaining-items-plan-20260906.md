# Kế hoạch xử lý các mục còn mở — perf-investigation (2026-09-06)

Nhánh: `chore/perf-investigation` · Dựa trên: `plans/perf-investigation-handoff.md` (§5, §6, §8) và
`docs/for-developers/known-limitations.md` (nguồn dữ liệu sống — tài liệu này không lặp lại bảng bằng
chứng của nó, chỉ tham chiếu).

Tài liệu này không chạy load test hay sửa code — chỉ định hướng thứ tự và tiêu chí "xong" cho từng mục.
Mỗi bước có dòng `→ verify:` theo quy tắc 4 của `CLAUDE.md`.

## Tóm tắt hiện trạng 4 mục còn mở

| # | Mục | Việc thật sự cần làm |
|---|-----|----------------------|
| A | Stress/soak chưa từng chạy thật | Profile + soak-smoke + stress ramp thật đã xong (§1); còn thiếu soak 12h thật |
| B | RC5 (SSE leak) chưa được đóng | Nhánh `sse` đã code xong + verify plumbing ở scale nhỏ (§2); vẫn cần soak 12h thật mới đóng được RC5 |
| C | RC11 — chưa từng bắt được thread dump | Là điều kiện tiên quyết trước khi chạy lại, không phải bước làm giữa chừng — xem §3 |
| D | 2 bug OpenAPI (`basilapi.json`) | ĐÃ XONG cả 2 — §4 sửa 2026-09-06, §5 tự resolve qua feature khác |
| E | RC8 (protocol allocation) | Giữ nguyên thứ tự cuối cùng của handoff — chỉ bắt đầu sau khi A-C xong |

## §1. Stress/soak: bật cờ, không phải "audit config gap"

**Đã xác minh trong session này** (handoff còn ghi là "chưa audit", giờ đã rõ):

- `tests/Basil.LoadTests/Profiles/full.json` có `Scenarios.stress.Enabled: false` và
  `Scenarios.soak.Enabled: false` — tường minh, chủ ý tắt.
- `StressScenario.cs:25` và `SoakScenario.cs:30` đều `if (!settings.Enabled || ...) return [];` — đúng
  hai nhánh điều kiện đó tạo ra dòng log "disabled or produced no variants; skipping." Không có gì để
  audit thêm; không phải bug.
- Cả hai đều bind qua `context.GetScenarioSettings<T>(Id)` chuẩn (property name khớp JSON key 1:1) —
  `SoakSettings.Weights`, `.ReportingIntervalSeconds`, `.LeakSlopeThresholds` đều có field tương ứng
  trong `ScenarioSettings.cs:199-220`, không có dấu hiệu lệch tên field.

**Việc cần làm:**

1. ~~Tạo 2 profile riêng~~ — XONG (2026-09-06). `Profiles/stress.json` hoá ra đã tồn tại sẵn từ harness
   gốc (commit `82d9e97`, không phải việc mới) — chỉ cần đổi `Port` 8443→9443 (xem note port bên dưới).
   `Profiles/soak-smoke.json` tạo mới, soak 300s thay vì 43200s, giữ nguyên `Weights`/`LeakSlopeThresholds`
   pattern. **Phát hiện phụ**: port 8443 đang nằm trong Windows dynamic TCP exclusion range hiện tại
   (`netsh int ipv4 show excludedportrange`, 8433-8532 — Hyper-V/WSL/Docker cấp lại) → bind
   `SocketException 10013`. Đổi toàn bộ 4 profile (`full.json`, `api-only.json`, `stress.json`,
   `soak-smoke.json`) sang port 9443. Không phải bug code.
   → verify: `dotnet run --project tests/Basil.LoadTests -- --profile soak-smoke` chạy hết vòng đời,
   report sinh ra ở `.loadtest/reports/` — **đã xác nhận, xanh**.
2. ~~Chạy soak-smoke trước~~ — XONG. 3 lần chạy đầu dính lỗi môi trường không liên quan code (xem vòng
   log tương ứng): seeding 404 hàng loạt + hang treo ở "Beatmap serving mode" — cả 2 do 1 process
   `dotnet Basil.Web.dll` orphan (tự start lúc debug port 8443 sớm hơn, port 9444) giữ lock chung file
   `Basil.db`/log với server thật do harness start. Xoá `.loadtest/server` + kill orphan process → chạy
   sạch. `soak-analysis.md` báo đúng "insufficient data (5min < 30min minimum)" cho mọi series — đúng dự
   kiến, KHÔNG phải "no leak", chỉ xác nhận plumbing hoạt động.
3. ~~Chạy `stress` ramp thật (100 → 5000)~~ — XONG (2026-09-06, report
   `.loadtest/reports/stress-20260906-090139/`, commit đang tạo). 32m23s, 484554 request, fail 2569
   (0.53%), không đạt ngưỡng "unusable" (>5% fail liên tục 60s) hay "saturation" (process CPU ≥95% liên
   tục 30s — process CPU chỉ đạt max 80.29%, mean 32.80%).
   **Phát hiện quan trọng**: `TotalMachineCpuPercent` (CPU toàn máy) áp sát 97-100% kéo dài suốt đoạn
   ramp 1000→2000 trở lên (`resources.csv`), dù CPU riêng của process server không hề bão hoà — vì máy
   test chạy CẢ server lẫn load-generator client trên cùng 8 logical core. Đây là giới hạn của thiết lập
   single-machine, không phải trần năng lực thật của server: `SaturationCpuPercent` trong `stress.json`
   chỉ theo dõi CPU riêng của process, nên không bắt được tình trạng nghẽn CPU toàn máy này — cần lưu ý
   khi đọc lại các mốc "an toàn" ở đây. Latency xấu đi rõ ở đỉnh tải: p99 29.2s, max 156.98s (ở mức
   1000-5000 đồng thời); ở mức thấp hơn latency vẫn tốt (p50 194ms toàn run). Không có tín hiệu RC11
   (threadpool queue length max 839 nhưng mean chỉ 103, tăng cùng lúc CPU toàn máy tăng — khớp mô hình
   tải tăng bình thường, không phải "CPU thấp mà queue vẫn phình" là dấu hiệu bất thường của RC11).
   → verify: `stress-events.md` — 1 `first-server-error` (ở mức 100, sớm, khả năng do JIT/GC warm-up),
   1 `first-connection-failure` + 1 `first-timeout` (cả hai ở ramp 1000→2000, khớp thời điểm CPU toàn máy
   chạm 100%) — **đã xác nhận qua `resources.csv`, không phải bug server**.
4. **Còn lại**: `soak` 12-24 giờ thật, có giám sát trực tiếp — xem §3 cho điều kiện tiên quyết trước khi
   bấm chạy. **Không được tự chạy không giám sát** — cần người theo dõi tiến trình trực tiếp và sẵn sàng
   bắt `dotnet-dump` nếu nghi RC11 (xem §3).
   **2 lần thử thất bại (2026-09-06/07), cả hai do máy hết RAM, không phải do server crash tự nhiên:**
   - **Lần 1** (750 đồng thời, `soak-full.json`): chạy 70 phút thì bị kill vì RAM hệ thống cạn (máy lúc đó
     chỉ ~1.6-4GB free). Handle count tăng ~833/giờ ngoại suy trong cửa sổ ngắn này — SUPPORTED cho RC5,
     chưa CONFIRMED (mẫu quá ngắn, không tách được leak thật khỏi áp lực RAM chung của máy).
   - **Lần 2** (1000 đồng thời, `soak-full-24h.json`, sau khi user giải phóng RAM lên ~7GB free): chạy được
     **13h42m/24h (57%)** trước khi bị kill. Dữ liệu process (qua `Get-Process`, không phải `resources.csv`
     — file đó chỉ ghi lúc kết thúc run, xem code `ResourceTimeline.WriteCsv`) cho thấy handle count tăng
     **giảm tốc dần** qua các mốc 3.5h liên tiếp: ~801 → 244 → 129 → 90 handle/giờ — hình dạng đường cong
     bão hoà (warm-up rồi ổn định), KHÔNG phải leak tuyến tính vô hạn. Working set cũng giảm tốc nhưng
     chậm hơn (~42MB/giờ ở đoạn cuối, vẫn trên ngưỡng `WorkingSetMbPerHour: 25`).
     **Sự kiện gây sập**: trong ~90 giây cuối (11:27:58→11:29:30 giờ máy ngày 07/09), working set của
     process nhảy đột biến 2.1GB → 3.47GB, health-check chuyển từ nhanh (10ms) sang treo hẳn (timeout 5s,
     `curl` trả `000`) rồi process biến mất hoàn toàn — quá nhanh để `dotnet-dump` kịp bắt (watcher báo lỗi
     "Invalid process id", process đã chết trước khi lệnh chạy). Đối chiếu `latest.log` cùng thời điểm: một
     đợt hàng trăm match cùng đóng qua timer phòng-trống-15-phút trong vài giây (`MatchId` 357150-357342,
     dồn cụm ở `11:30:00-11:30:12`) — vì kịch bản `multiplayer` của soak tạo phòng liên tục suốt nhiều giờ
     mà timer 15 phút không có jitter, các phòng tạo ra trong cùng khung giờ (vd lúc warm-up) sẽ hết hạn
     cùng lúc, tạo ra đợt dọn dẹp dồn cục định kỳ. **HYPOTHESIS**: đợt dọn dồn cục này là tác nhân gây tăng
     đột biến bộ nhớ/GC ngay trước khi sập, cộng dồn vào baseline bộ nhớ vốn đã cao (do RAM hệ thống lúc đó
     cũng chỉ còn ở mức trung bình) — chưa xác nhận được đây là nguyên nhân gốc hay chỉ là trùng thời điểm,
     vì tiến trình harness giám sát cũng bị hệ thống chủ động kill do cảnh báo thiếu RAM tại đúng lúc đó
     (không phải OOM-kill của riêng process server). Không loại trừ được RC11 hay một leak khác chưa đặt
     tên đứng sau đợt tăng đột biến này — cần chạy lại có `dotnet-dump` áp sát hơn (poll nhanh hơn 30s) để
     bắt kịp lần tới.
     **Khuyến nghị nếu chạy lại**: (a) đảm bảo máy có nhiều RAM trống dư hơn (>8GB) trước khi bắt đầu vì cả
     2 lần đều liên quan tới RAM hệ thống mỏng; (b) cân nhắc thêm jitter ngẫu nhiên vào timer phòng-trống
     nếu đợt dọn dồn cục này được xác nhận là vấn đề thật (không chỉ ảnh hưởng soak mà cả production thật
     có thể có nhiều phòng tạo cùng lúc, vd giờ cao điểm giải đấu); (c) poll watcher nhanh hơn (10-15s thay
     vì 30s) để có cơ hội bắt `dotnet-dump` trước khi process chết trong các đợt tăng đột biến nhanh.

## §2. RC5 (SSE leak) — soak hiện tại không đóng được mục này

`known-limitations.md` ghi RC5 "cần cùng một lần chạy `full.json` có giám sát" như mục capacity phía
trên. Điều đó **không đúng** với cấu hình soak hiện tại:

- `soak.Weights` trong `full.json` chỉ có `chat/multiplayer/api/idle` — không có `sse`.
- `SoakScenario.BuildWeightedActions`/switch (`SoakScenario.cs:74-107`) không có nhánh `"sse"` — dù có
  thêm key `sse` vào `Weights`, switch cũng rơi vào `default` (idle), không làm gì khác.
- Scenario `sse` riêng trong `full.json` cũng đang `Enabled: false` và bản thân nó không mô phỏng đúng
  kịch bản RC5 cần (client giữ kết nối SSE xuyên qua lúc match đóng/mở lại — "match close/reopen churn").

**Việc cần làm (việc code thật, không chỉ đổi config):**

1. ~~Thêm nhánh "sse"~~ — XONG (commit `5ef80ad`). Subscribe `/matches/{id}/settings/live`, đóng match
   bằng `POST /matches/{id}/close` (KHÔNG phải `PartMatch` — rời ghế chỉ để phòng trống chờ timer 15
   phút, không đóng ngay, xem `multiplayer.md`), tạo+đóng match thứ 2, rồi drain stream tới EOF thật
   hoặc deadline 15s. 2 bug tự tìm ra khi làm thật:
   - Đọc 1 dòng để "confirm còn sống" → sai, SSE nhiều dòng/event, dòng sót trong buffer làm lần đọc
     sau luôn non-null → false "still-open". Sửa: drain loop tới EOF thật.
   - Đóng bằng `PartMatch` ban đầu → không bao giờ trigger `Writer.Complete()` (không đóng phòng thật).
     Sửa: dùng `POST /matches/{id}/close`.
2. ~~Thêm "sse" vào Weights~~ — XONG, `soak-smoke.json` + `full.json`.
   → verify: **đã chạy thật** — sau khi sửa cả 2 bug, `soak-smoke` 5 phút báo `sse-closed` cho toàn bộ
   14/14 lần action `sse` chạy (trước khi sửa: 26/26 và 21/21 lần đều `sse-still-open` giả do bug #1
   rồi bug #2). Xác nhận cơ chế `Writer.Complete()` hoạt động đúng ở scale nhỏ.
3. **Còn lại thật sự**: soak 12 giờ thật với nhánh này bật — chỉ khi đó mới đối chiếu được
   working-set/GC-heap slope của `SoakAnalyzer` để đóng RC5 (`CONFIRMED` nếu vượt threshold, hoặc
   `CLOSED — no leak observed` nếu không). 5 phút soak-smoke KHÔNG đủ dữ liệu (`SoakAnalyzer` tự báo
   "insufficient data (5min < 30min minimum)") — đây là xác nhận plumbing hoạt động, KHÔNG phải kết
   luận RC5 đã đóng hay chưa. Đừng lặp lại nhầm lẫn "soak ngắn = no leak" lần ba
   (`known-limitations.md` đã ghi nhận nhầm lẫn này 2 lần trước với run 93 giây).

## §3. RC11 — chuẩn bị bắt thread dump là điều kiện tiên quyết, không phải bước giữa chừng

3 lần thử ở 2 đợt điều tra đều không bắt được thread dump — vì công cụ chưa sẵn sàng lúc sự cố xảy ra.
Cửa sổ sập từng rộng 6 phút; không đủ thời gian để cài công cụ giữa chừng.

**Trước khi chạy `stress`/`soak` thật (không phải trong lúc chạy):**

1. ~~Xác nhận `dotnet-dump` đã cài~~ — XONG (2026-09-06): `dotnet-dump` 10.0.731102 đã cài sẵn
   (`dotnet tool install --global dotnet-dump` báo "already installed"). Còn lại: biết PID tiến trình
   server trước khi bấm chạy lần tới (`dotnet-dump collect` cần PID, không tra được giữa lúc sập).
2. Viết sẵn lệnh bắt dump để copy-paste ngay khi thấy dấu hiệu sập (`ThreadPoolQueueLength` tăng liên
   tục trong khi `CpuPercent` giữ gần 0 qua vài mẫu liên tiếp):
   ```bash
   dotnet-dump collect -p <pid>
   dotnet-dump analyze <dump-file>
   > clrstack -all
   ```
3. Theo dõi `resources.csv` **trực tiếp** (không chờ report cuối) trong suốt lần chạy `stress`/`soak`
   thật — đây là hành động cần làm chủ động, không phải phản ứng sau khi đọc log.

→ verify: trước khi lần chạy giám sát tiếp theo bắt đầu, cả 3 mục trên đã sẵn sàng (checklist, không
phải tùy ứng biến).

## §4. Dangling `$ref` trong `basilapi.json` — ĐÃ SỬA (commit `611ed36`)

Vị trí: `OpenApiExampleExtensions.cs`'s `WithMainLiveExamples` (dòng 119-126) và `WithUserLiveExamples`
(dòng 170-177). Cả hai tự tay dựng `OpenApiSchemaReference("<TênType>", context.Document)` cho **cả hai**
type trong cặp `.Produces<T>()` — nhưng chỉ type khai báo **sau cùng** trong cặp thực sự có schema được
đăng ký vào `components.schemas` (đã xác nhận: `MatchLiveSnapshot`/`PlayerStatusView` — khai báo đầu —
không có schema nào đăng ký; `PlayerLiveScore`/`SpectateFramesEvent` — khai báo sau — đăng ký đúng).

**Đã loại trừ:** đổi thứ tự hai lệnh `.Produces<T>()` — chỉ dời dangling ref sang type còn lại, không
sửa gì cả.

**Hướng sửa thật sự** (cần xác minh API cụ thể trước khi code — chưa biết chắc `Microsoft.OpenApi`
2.11.0 / ASP.NET Core OpenAPI expose API nào để ép đăng ký component schema theo kiểu C# runtime, ví dụ
qua `IOpenApiSchemaTransformer`, `JsonSchemaExporter`, hay method nội bộ khác):

1. Trong `WithMainLiveExamples`/`WithUserLiveExamples`, trước khi build `OneOf` bằng
   `OpenApiSchemaReference`, đảm bảo **cả hai** type đã có schema trong
   `context.Document.Components.Schemas` — nếu type khai báo đầu chưa có, tự tạo và chèn vào (không giả
   định `.Produces<T>()` đã làm việc đó).
2. `WithSlotLiveExamples` (dòng 229-246) **không cần sửa** — chỉ có một `.Produces<PlayerLiveScore>()`
   duy nhất, tái dùng `mediaType.Schema` hợp lệ (dòng 237). Đừng đụng vào nó.

**Tiêu chí "xong"** (không phải "code chạy được" — phải verify bằng đúng công cụ đã dùng để phát hiện
bug này):

→ verify: build lại (schema OpenAPI sinh lúc build qua `Microsoft.Extensions.ApiDescription.Server`),
chạy lại validator tạm (`OpenApiDocument.Parse` + `Diagnostic.Errors`) trên **cả 6 tài liệu**
(`bancho`, `osuweb`, `beatmapassets`, `avatar`, `assets`, `basilapi`) — không chỉ `basilapi` — vì thay
đổi cách đăng ký component có thể ảnh hưởng tài liệu khác. Yêu cầu: 0 `Diagnostic.Errors` ở cả 6, và
`basilapi.json`'s `components.schemas` chứa cả `MatchLiveSnapshot` và `PlayerStatusView` (mỗi tên xuất
hiện ở đúng 1 định nghĩa component, không chỉ ở `$ref`).

**ĐÃ XONG (2026-09-06)**: sửa bằng `context.GetOrCreateSchemaAsync(typeof(T))` (.NET 10) thay vì tự dựng
`OpenApiSchemaReference` theo tên — type bị mất giờ trả về schema inline đầy đủ (không phải component
`$ref`, nhưng đó là đúng vì không type nào trong 2 type này dùng lại ở operation khác). Verify: quét
dangling `$ref` (JSON parse + so khớp `components.schemas`) trên cả 6 tài liệu → 0 dangling ở tất cả.
Regression test `BasilApiDocument_SseRouteUnsharedPayloadType_IsNotADanglingRef`, xác nhận fail đúng lý
do trên code cũ (revert-and-fail). Commit `611ed36`.

## §5. Duplicate path template — đã tự resolve (2026-09-06)

Đã xử lý như một phần của feature "chỉ dùng id cho `/users`, thêm `GET /users/search`" (yêu cầu riêng
của bạn, không nằm trong scope perf-investigation ban đầu): `GET /users/{idOrName}` (và `/avatar`,
`/live`) đổi thành `GET /users/{userId:numericid}` — không còn resolve username qua path parameter nữa.
Việc tra username giờ tách riêng qua `GET /users/search`.

Xác nhận trên `basilapi.json` sinh ra sau thay đổi: `/users/{userId}`, `/users/{userId}/avatar`,
`/users/{userId}/live` mỗi cái chỉ còn đúng 1 template, dùng chung tên tham số giữa GET và
PUT/PATCH/DELETE — không còn vi phạm OpenAPI 3.x nào để quyết định nữa.

## §6. RC8 (protocol allocation) — giữ nguyên vị trí cuối cùng

Không đổi so với khuyến nghị của handoff: chỉ bắt đầu re-profiling sau khi §1-§3 cho một capacity
ceiling thật để nhắm vào. Không tối ưu đầu cơ trước đó.

## Thứ tự thực hiện tổng thể

1. §1 bước 1-2 (tạo profile, chạy soak-smoke) — rẻ, làm trước.
2. §2 bước 1-2 (thêm nhánh `sse` vào soak) — làm cùng lúc với §1 vì cùng đụng `SoakScenario.cs`/profile.
3. §3 (chuẩn bị `dotnet-dump`) — làm song song, không phụ thuộc §1/§2, nhưng phải xong **trước** bước 4.
4. Chạy `stress` ramp thật + `soak` 12 giờ thật, có giám sát trực tiếp `resources.csv`.
5. ~~§4 (sửa dangling `$ref`)~~ — XONG, commit `611ed36`.
6. ~~§5~~ — XONG, tự resolve qua feature `/users` chỉ dùng id (commit `5f1264a`).
7. RC8 — chỉ sau bước 4 có kết quả.

Việc còn lại thật sự chỉ còn: §1-§3 (stress/soak + RC5 SSE branch + chuẩn bị dump), rồi mới tới RC8.
