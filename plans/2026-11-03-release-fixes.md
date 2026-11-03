# Kế hoạch: sửa toàn bộ phát hiện review + Default User Permissions (osuBasil, `develop`)

## Context
Review 2026-11-03 (`~/.claude/plans/review-2026-10-30/REVIEW.md`) kết luận chưa release được: R1/R2 restart, C1–C14,
M1–M10, các quyết định kiến trúc 6.1–6.7 của owner, mục còn mở của issue #4, CodeFactor 35 minor, ~120 Medium/Low chưa
xác minh. Owner muốn sửa hết, nhanh, ít đợt, song song agent, mục tiêu ~1k user đồng thời trên máy owner (không cần load
test). Thêm mới: setting trong DB đặt **quyền mặc định của tài khoản mới** khi người tạo không nêu quyền.

Quyết định owner (phiên này): C6 giữ công khai (chỉ ghi tài liệu); Default permissions chỉ cần `ServerManageSettings`;
không load test; tên thư mục `Ports/` + `Logic/` (Infrastructure `Adapters/`); ImageSharp 3.1.x suppress có chú thích +
giảm thiểu; agent: OpenCode flash cho cơ học, Sonnet cho khó; mỗi đợt commit + **push** develop, điều phối viên theo dõi
CI và CodeFactor tới xanh.

## Cách chạy
- 3 đợt. Mỗi đợt: `dotnet build Basil.slnx -c Release` + toàn bộ project test từng cái một
  (`~/.claude/plans/review-2026-10-30/work/run-tests.sh`) + `tests/Architecture` container check → commit theo chủ đề
  (stage đường dẫn cụ thể, không `Basil.sln.DotSettings`) → push → `gh run watch` + CodeFactor (commit status:
  `gh api repos/thnhmai06/osuBasil/commits/<sha>/status` và `…/check-runs`) → sửa tới xanh.
- Agent: tối đa 2 cùng lúc, worktree riêng, file rời nhau, prompt = file + exit marker + timeout; tôi đọc diff theo mục
  "Final verification" 7–8 của AGENTS.md rồi merge.
- Microsoft Learn MCP dùng để chốt API: `SystemdHelpers`, Kestrel `Limits`/`AddServerHeader`, `BadHttpRequestException`,
  `Channel.CreateBounded` + `BoundedChannelFullMode`, `NpgsqlDataSource` đọc/ghi tách, logging file provider.

## Đợt 1 — đổi tên/di chuyển hàng loạt (tôi làm, tuần tự, vì chạm gần hết repo)
1. **By → Actor** (6.5): Rider `rename_refactoring` cho `Scoped<T>.By`, event record `By`, tham số `by`,
   `MatchEvent.By`, `CommandContext.By`, `call.ByAsync`; JSON view đổi `by`→`actor` — **đổi hợp đồng API, cố ý**
   (trước release; cập nhật OpenAPI, Postman, `docs/for-client`). Docs: AGENTS.md, `application-design.md`, `identity-permissions.md`, skill `code-principles`.
2. **Capabilities chỉ còn `Scoped*`** (6.3): port ngoài → `Ports/` (`IBeatmapAnalyser`, `IBeatmapAssets`,
   `IBeatmapsetMirror`, `IBeatmapsetReader`, `IScreenshotInspector` + dữ liệu trao đổi); logic object-free →
   `Logic/` (`BeatmapsetImporter/Scanner/Planner/Matcher/Storer/FileAudit/FileWriter`, `DifficultyAnalyser`,
   `ScoreRecorder`, `AccountService`, `CredentialChecks`, `SetClaims`, `StoredFileNames`); `ScoreOutcome` → `Objects/`;
   Infrastructure `Capabilities/` → `Adapters/`. Namespace theo thư mục; test mirror cùng đổi; AGENTS.md bảng thư mục,
   `architecture.md`, `application-design.md`.
3. **`Program` thành class** (6.6): `public static class Program { public static async Task<int> Main(string[] args) }`.
4. Sửa cảnh báo build: CS1573 `ServerContext.cs:34-40`, CS8604 `ShutdownEndToEndTests.cs:104,157`, xUnit1051.
→ build + test + push + CI.

## Đợt 2 — hai track Sonnet song song + tôi làm phần rời
**Track A (Sonnet, worktree, chỉ `src/Hosts/**` + test tương ứng)** — runtime/bảo mật/hiệu năng:
- R1: `Restarter.UnderManager()` dùng `Microsoft.Extensions.Hosting.Systemd.SystemdHelpers.IsSystemdService()` (cha là
  systemd) hoặc container; bỏ `INVOCATION_ID`. R2: relaunch bằng `Environment.GetCommandLineArgs()` (giữ dll khi host là
  `dotnet`). Test hồi quy qua seam môi trường/tiến trình.
- C1: `WebAuthentication` giữ cache trong Host.OsuStable: `ConditionalWeakTable<OsuStableConnection, Md5>` lưu MD5 đã verify
  BCrypt thành công (sống cùng kết nối; đổi mật khẩu đóng session ⇒ kết nối mới ⇒ tự hủy); request sau so sánh
  `CryptographicOperations.FixedTimeEquals`. Không thêm state vào object Application. Tra user theo mẫu sẵn có
  `users.GetByNameAsync(...)` + `registry.Of(record)` (như `PacketHandlers.Chat.cs` `PrivateMessageAsync`), bỏ quét
  `Online.Values`.
- C2: IRC `Channel.CreateBounded` + OsuStable `Outbox` có trần (số packet/bytes); vượt trần ⇒ đóng kết nối (client chậm).
- C3: `RateLimiter` dọn key rỗng/hết hạn (quét định kỳ theo `TimeProvider`, giới hạn số key).
- C4: Kestrel `AddServerHeader=false`, `MaxRequestBodySize` theo `Uploads.OszMaxSize` cho route upload (giới hạn chung
  nhỏ), HSTS khi HTTPS công khai. Không thêm giới hạn kết nối toàn cục (đã có giới hạn theo IP trong settings).
- C5: osu!direct search/set/download lọc `Hidden` + `LimitTo` người hỏi. C7: `GET /v1/users` `.LimitTo(effective)`.
- C6: chỉ ghi `.WithDescription` rằng hồ sơ, quyền và lịch sử restriction là công khai.
- C9: đặt `RouteHandlerOptions.ThrowOnBadRequest = true` cho **mọi** môi trường (mặc định false ngoài Development ⇒
  framework tự trả 400 rỗng, catch không bao giờ chạy), rồi `RestPipeline` bắt `BadHttpRequestException` ⇒ envelope
  400 (`invalid`).
  Kèm issue #4 mục 1, 2, 14, #7 (số tràn ⇒ lỗi validation).
- C11: `UserPresenceRequest`/`StatsRequest` tra id qua dictionary của registry, giới hạn số id/packet; `BeatmapInfoRequest`
  giới hạn số filename + một truy vấn gộp.
- M1 `SseHub` snapshot dưới khóa (hoặc `ImmutableArray`); M7 `gc/collect` giới hạn tần suất. (M6 `src/Bot` ngoài
  ranh giới track: tôi làm.)
- Issue #4 phía REST: JSON không escape Unicode (`JavaScriptEncoder.UnsafeRelaxedJsonEscaping` trong `ApiJson`),
  trang ngoài phạm vi ⇒ trang rỗng.

**Track B (Sonnet, worktree, `src/Domain`, `src/Application/**`, `src/Infrastructure/**` trừ Common/Persistence config)**:
- 6.1 + 6.2: `Session` Domain thành `SessionRecord` + `SessionData` bất biến, chia nhóm (định danh, thiết bị +
  `DeviceId` `readonly record struct` trong Domain dùng `Md5`, credential, kết thúc); state hoạt động (last active, sliding
  expiry, reauth) lên Application (`Connection`/registry), ghi bản sao qua repository. Sửa F9 (ghi không nguyên tử).
- M3: ghi session có await/log lỗi qua writer queue, không `_ =` im lặng. M4: thay `ReplaceAfterIdle` bằng hỏi
  `IsOpen` như tài liệu. M5: migration unique index `sessions.token_hash`.
- C8: `PostgresRoundScoreRepository` chỉ gỡ `_pending` khi ghi thành công và đúng instance (`TryRemove(KeyValuePair)`).
- C10: tách `MatchRecorder` thành một handler mỗi loại event. `DatabaseWriter` có hai đường: `WriteAsync` (chờ DB, trả
  id — `MatchRecorder` dùng 27 lần) và `SaveAsync` qua `ResidentWrites` (xếp hàng). Tách handler chỉ hết chặn hàng đợi
  event khi đường event không còn cần id do DB cấp: prompt yêu cầu agent đề xuất trước (id cấp ở memory/sequence,
  hay ghi match/round không cần id trả về) rồi tôi duyệt mới code.
- M2: handler Chat cho `RoomRefereeRemoved` part referee không ngồi slot khỏi `#mp_`.
- C14: `ScopedServerScreenshots` đọc `Uploads.ScreenshotMaxSize/MaxSide`; upload `.osz` đọc `Uploads.OszMaxSize`.
- **Default User Permissions**: `ServerSettings.Accounts` (`AccountSettings { UserPermissions DefaultPermissions =
  UserPermissionsGroups.Default }`), category `accounts` trong `PostgresSettingsRepository.Categories` (hàng JSON, không
  cần migration). `UserData.Permissions` giữ initializer `UserPermissionsGroups.Default` (giá trị gốc của setting).
  `AccountService.CreateAsync` nhận `UserPermissions?`; `null` ⇒ `data with { Permissions = settings.Accounts.DefaultPermissions }`. Dùng ở
  `UserRegistry.RegisterAsync` (đăng ký từ client) và `ScopedUser…CreateAccountAsync`. Quyền đổi: `ServerManageSettings`
  như mọi setting. Test: đổi setting ⇒ tài khoản đăng ký mới nhận đúng mask; quyền tường minh thắng; mặc định = giá trị cũ.

**Tôi (song song, file rời):** C12 release dùng `tar.gz` cho Linux (giữ bit thực thi); C13 `stop_grace_period: 90s`;
M8 CI chạy từng project test; M9 `NuGetAuditSuppress` ImageSharp có chú thích + giới hạn decode PNG/JPEG đã dò;
M10 `HEALTHCHECK` + user không root trong `Dockerfile`. OpenCode flash: xác minh lại ~120 Medium/Low
(`_index-all-findings.txt`) trên code hiện tại ⇒ danh sách xác nhận cho đợt 3.

Sau khi hai track xong: tôi merge, nối phần host của Default permissions (`UserRoutes.CreateAsync` và CLI
`users create` truyền `null` khi không nêu quyền; `SettingsCategories` thêm `accounts` — lệch sẵn có `menu_icon` thiếu
ở phía host giữ nguyên, không "sửa" tiện tay; M6 `ChatHandler` chỉ đẩy tin bắt đầu bằng prefix; OpenAPI/`configuration.md`/
`identity-permissions.md`), container check, build + test + push + CI + CodeFactor.

## Đợt 3 — hai track Sonnet song song + tôi
**Track C (Sonnet)** — 6.4 enum Domain tách wire: Protocol.OsuStable khai báo enum wire (Mods, Privileges, GameMode,
ranked status, slot status, team, team type, win condition, action, login reply, country) + ánh xạ hai chiều có test
byte-pair; Domain giữ giá trị tường minh ổn định (DB lưu Domain). Quét mọi cast Domain↔Protocol trong `Host.OsuStable`.
Kiểm `UserPrivileges.Tournament` trùng bit `mode << 5`. Sửa `identity-permissions.md` ("values stay wire bits").
**Track D (Sonnet)** — 6.7 appsettings theo `~/Desktop/basil/example_appsettings.json`: Kestrel chuẩn
(`Endpoints`/`Certificates`), `Basil:Server{Name,Domain,TrustedProxies,Irc.Port,Multicast{…, Enabled=false mặc định}}`,
`ConnectionStrings:Read/Write` ⇒ hai `NpgsqlDataSource` (reader/writer) + `Database.Read/Write.PoolSize`;
`Database.Enabled=false` hoặc Provider ≠ PostgreSQL ⇒ lỗi khởi động rõ ràng; `Storage.Location`; `Logging:LogLevel` +
`Logging:File` (rolling, retention); `OpenTelemetry`; `Updates.Channel`. File ship không comment/dấu phẩy thừa, mật khẩu
`CHANGE_ME`. Cập nhật `appsettings.schema.json`, `configuration.md`, `docker-compose.yml`, test cấu hình.
**Tôi:** mục issue #4 còn mở phía Bot (`!mp mods` lọc theo mode, `!mp map` playmode, ghi đè timer báo hủy), Medium/Low
đã xác nhận, CodeFactor 35 minor (đọc từ check run), ghi chú nâng cấp từ `v1.0.0-alpha.1`.
→ merge, container check, build + test + push + CI + CodeFactor xanh; cập nhật REVIEW.md trạng thái từng mục.

Không làm: UI CLI (owner làm sau), R3 (cùng đợt UI), SSE full-match gameplay (tính năng mới — sẽ hỏi nếu còn thời gian).

## Kiểm chứng
- Mỗi lỗi tái hiện được có test hồi quy trước khi sửa (TDD); refusal test kiểm mask thiếu.
- Chạy binary thật cuối đợt 2 và 3 (Postgres Docker): `users create` offline, login + một request mỗi giao thức, JSON
  hỏng ⇒ envelope 400, `restart --now` qua `./Basil.Host` và `dotnet Basil.Host.dll` (có và không `INVOCATION_ID`) ⇒
  tiến trình mới, SIGINT ⇒ exit 0, đăng ký tài khoản sau khi đổi `accounts.defaultPermissions` ⇒ đúng mask.
- CI GitHub xanh + CodeFactor không còn issue mới sau mỗi push.
