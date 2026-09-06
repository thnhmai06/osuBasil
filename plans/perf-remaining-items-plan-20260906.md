# Kế hoạch xử lý các mục còn mở — perf-investigation (2026-09-06)

Nhánh: `chore/perf-investigation` · Dựa trên: `plans/perf-investigation-handoff.md` (§5, §6, §8) và
`docs/for-developers/known-limitations.md` (nguồn dữ liệu sống — tài liệu này không lặp lại bảng bằng
chứng của nó, chỉ tham chiếu).

Tài liệu này không chạy load test hay sửa code — chỉ định hướng thứ tự và tiêu chí "xong" cho từng mục.
Mỗi bước có dòng `→ verify:` theo quy tắc 4 của `CLAUDE.md`.

## Tóm tắt hiện trạng 4 mục còn mở

| # | Mục | Việc thật sự cần làm |
|---|-----|----------------------|
| A | Stress/soak chưa từng chạy | Không phải bug config — chỉ là cờ tắt (`Enabled: false`), xem §1 |
| B | RC5 (SSE leak) chưa được đóng | Soak hiện tại **không** phủ được kịch bản này dù stress/soak chạy xong — cần thêm việc, xem §2 |
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

1. Tạo 2 profile riêng theo đúng khuôn mẫu `Profiles/api-only.json` đã có (profile con để lặp có kiểm
   soát, thay vì bật thẳng trong `full.json` và chạy toàn bộ tuần tự mỗi lần):
   - `Profiles/stress.json` — chỉ bật `stress`, các scenario khác tắt.
   - `Profiles/soak-smoke.json` — chỉ bật `soak`, `DurationSeconds` rút xuống ~300 (5 phút) thay vì
     43200, giữ nguyên `Weights`/`LeakSlopeThresholds`.
   → verify: `dotnet run --project tests/Basil.LoadTests -- --profile stress` và
   `--profile soak-smoke` đều tạo ra scenario variant (không còn dòng "produced no variants"), chạy hết
   vòng đời, và report sinh ra ở `.loadtest/reports/`.
2. Chạy `soak-smoke` **trước** khi chạy soak 12 giờ thật. Đây là bước rẻ nhất để tránh phát hiện lỗi bind
   hoặc lỗi `SoakAnalyzer` ở giờ thứ 11 của một lần chạy 12 giờ.
   → verify: report có đủ metric cho `SoakAnalyzer` tính slope (working set, GC heap, thread/handle
   count, P95), và không có exception nào từ chính scenario code (`ex.GetType().Name` trong log).
3. Chỉ sau khi (1) và (2) xanh: chạy `stress` ramp thật (100 → 5000) rồi `soak` 12 giờ thật, có giám sát —
   xem §3 cho điều kiện tiên quyết trước khi bấm chạy.

## §2. RC5 (SSE leak) — soak hiện tại không đóng được mục này

`known-limitations.md` ghi RC5 "cần cùng một lần chạy `full.json` có giám sát" như mục capacity phía
trên. Điều đó **không đúng** với cấu hình soak hiện tại:

- `soak.Weights` trong `full.json` chỉ có `chat/multiplayer/api/idle` — không có `sse`.
- `SoakScenario.BuildWeightedActions`/switch (`SoakScenario.cs:74-107`) không có nhánh `"sse"` — dù có
  thêm key `sse` vào `Weights`, switch cũng rơi vào `default` (idle), không làm gì khác.
- Scenario `sse` riêng trong `full.json` cũng đang `Enabled: false` và bản thân nó không mô phỏng đúng
  kịch bản RC5 cần (client giữ kết nối SSE xuyên qua lúc match đóng/mở lại — "match close/reopen churn").

**Việc cần làm (việc code thật, không chỉ đổi config):**

1. Thêm nhánh `"sse"` vào `SoakScenario`'s `Build`/switch: instance giữ một kết nối SSE tới
   `/matches/{matchId}/live` (hoặc `/users/{idOrName}/live`) xuyên qua ít nhất một chu kỳ đóng/mở match
   (map "multiplayer" action hiện tại — create → part — đã có sẵn logic tạo/đóng match, có thể tái dùng).
2. Thêm `"sse": <weight>` vào `soak-smoke.json` và `full.json`'s `soak.Weights`.
   → verify: `soak-smoke` chạy có action `"sse"` xuất hiện trong report (không rơi vào `default`).
3. Sau khi soak 12 giờ thật chạy xong với nhánh này bật: đối chiếu working-set/GC-heap slope của
   `SoakAnalyzer` — nếu vượt `LeakSlopeThresholds`, RC5 được xác nhận (`CONFIRMED`), có bằng chứng slope
   cụ thể để đưa vào `known-limitations.md`.
   → verify: `known-limitations.md`'s RC5 entry chuyển từ `NEEDS EXPERIMENT` sang `CONFIRMED` hoặc
   `CLOSED — no leak observed`, kèm số liệu slope thật.

Nếu không muốn mở rộng scope harness ở vòng này, ít nhất ghi rõ trong `known-limitations.md` rằng soak
hiện tại **không** phủ RC5, để người sau không lặp lại giả định sai này lần ba.

## §3. RC11 — chuẩn bị bắt thread dump là điều kiện tiên quyết, không phải bước giữa chừng

3 lần thử ở 2 đợt điều tra đều không bắt được thread dump — vì công cụ chưa sẵn sàng lúc sự cố xảy ra.
Cửa sổ sập từng rộng 6 phút; không đủ thời gian để cài công cụ giữa chừng.

**Trước khi chạy `stress`/`soak` thật (không phải trong lúc chạy):**

1. Xác nhận `dotnet-dump` đã cài và biết PID tiến trình server trước khi bấm chạy:
   ```bash
   dotnet tool install --global dotnet-dump   # nếu chưa có
   dotnet-dump --version
   ```
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
