# Plan: Infrastructure + BasilBot Application

## Context

Domain, Application, Protocol đã xong trên `develop`. Bước tiếp: viết lại Infrastructure (phần phụ thuộc bên thứ
ba: SQLite, file, osu! rulesets, ffmpeg, HTTP, vòng lặp nền, phân phối event) theo contract mới. Code cũ (`40990186`)
chỉ để tham chiếu tính năng: SQL cũ lệch tên cột ở ~6 repository, nhiều phần thuộc host.

Quyết định người dùng: bỏ qua Hosts và tests đợt này; BasilBot là Application riêng dùng `Basil.Domain`, adapter
HTTP/SSE làm cùng Hosts; DB baseline mới; xây cho trường hợp tổng quát, không tự giải từng trường hợp riêng.

## P0 — Sửa Application/Domain

1. **Id do kho cấp, khuôn `X { Id, Value: XData }`.** `Beatmapset`/`Beatmap` chuyển sang `IWrapper` với
   `BeatmapsetData`/`BeatmapData`. Repository `CreateAsync(XData, int? onlineId = null) → X`: kho dùng id online khi
   có, không thì cấp id local ≥ 1 000 000 000; `CreateOrUpdateAsync(X)` để sửa. `BeatmapsetService` bỏ
   `NewLocalIdAsync`, chỉ quyết định tạo mới hay cập nhật.
2. **N1** `ScoreService` đối chiếu với client của chính kết nối nộp điểm (`connection.Login.Client`), bỏ
   `ILoginRepository` khỏi service.
3. **N2** Đếm giờ đóng phòng giải bắt đầu khi người cuối rời ghế, chỉ hủy khi có người thật sự ngồi vào slot; vào hụt
   (`Full`) không hẹn giờ lại, không phát lại thông báo.
4. **N3** Sảnh hết chỗ: không ghi `Match`, không tạo `Room`, trả `RoomResult.NoRoomId` (giữ chỗ id phòng trong sảnh
   trước khi ghi `Match`, trả chỗ nếu ghi lỗi).
5. **Actor của match event.** Thêm `User By` vào `RoomPlayerKicked`, `RoomPlayerBanned`, `RoomHostChanged`,
   `RoomRefereeAdded`, `RoomRefereeRemoved`, `LobbyRoomClosed` (+ `bool Expired` khi đóng do phòng trống, lúc đó `By`
   null); `MatchRecorder` ghi vào `MatchEvent.Actor`.
6. **Beatmapset không có actor.** `IBeatmapsetService.ImportAsync(Stream archive, int? beatmapsetId)` bỏ `by` và các
   kiểm khóa/quyền beatmapset (host gác bằng policy như bảng 1.4 identity plan); bỏ
   `BeatmapsetImportFailure.NotAuthorized`. Watcher `Data/Imports` (debounce 2 s) nhập `.osz` được thả vào, xóa file
   khi thành công.
7. AGENTS.md: anticheat → "clients that watch it (BasilBot)"; creation lock của beatmapset do host gác; bảng kiến
   trúc theo các project mới; mục BasilBot.

## Cấu trúc project (đề xuất, soi theo Application)

```
src/Infrastructure/
  Storage/   Basil.Infrastructure.Storage   → Storage.Contracts   repository SQLite + file storage + migration
  Services/  Basil.Infrastructure.Services  → Services.Contracts  cổng năng lực: reader, analyser, assets, mirror
  Runtime/   Basil.Infrastructure.Runtime   → Services.Contracts  pump event, handler, ghi trận, vòng lặp nền, khởi động
src/Bot/
  Application/ Basil.Bot.Application        → Domain              logic BasilBot + cổng của bot
```

Mỗi project `internal sealed` + `DependencyInjection.AddInfrastructure{Storage,Services,Runtime}()` /
`AddBotApplication()`. Lý do: gói nặng (ppy rulesets ~50 DLL) chỉ ở Services; Storage chỉ thấy Storage.Contracts nên
repository không gọi được service; mỗi project ít gói. Diagnostics cũ là plumbing của API → chuyển sang
`src/Hosts/Api` chờ migrate host. Constructor DI không mở DB, không chạm file.

## Infrastructure

**Storage** (`Microsoft.Data.Sqlite`, `Dapper`, `BCrypt.Net-Next`): DB `Data/Basil.db`, migration bằng
`PRAGMA user_version` + SQL nhúng (thay DbUp), baseline mới một file; row DTO + map tay; thời gian = Unix ms UTC.
Bảng: Users (SafeName unique), Credentials, Logins, Restrictions, Relationships, Channels (seed `#osu`, `#lobby`),
Settings (một dòng có kiểu), MenuBanners, Beatmapsets, Beatmaps (Hash unique), Matches, Rounds (PK Match+Number),
MatchEvents, Scores (Checksum unique → trùng trả null), UserStats. Repository cho đủ 15 cổng; file storage cho
beatmapset (`{id} {Artist} - {Title}.osz`), replay (byte thô), avatar, banner, icon, seasonal, FAQ — mọi tên file qua
một hàm kiểm đường dẫn (chặn rỗng, `.`, `..`, rooted, thoát thư mục gốc).

**Services** (`ppy.osu.Game.Rulesets.*`): reader (.osz → metadata + difficulty), analyser (port `PpyOsuCalculator`,
đọc `byte[]`), assets (tìm `.osu` theo MD5 trong `.osz`; asset của set lấy từ beatmap id nhỏ nhất; cache file trong
`Data/Cache`; preview mp3 qua `ffmpeg` bằng `Process`; archive không video), mirror (một `HttpClient`, endpoint từ
`ServerSettings`).

**Runtime** (chỉ `Hosting.Abstractions`):
- `EventPump<T>`: một pump cho mỗi nguồn (8 nguồn, `UserEvent` có 2), đọc cạn mọi luồng, gọi lần lượt các
  `IEventHandler<T>`, bắt lỗi từng handler; host và adapter bot sau này đăng ký handler của mình.
- Handler: kết nối mở → vào kênh tự động; kết nối đóng → rời phòng → ngừng spectate → rời sảnh → rời kênh; điểm có
  phòng → ghi vào round; `MatchRecorder` (hàng đợi ghi tuần tự, thử 3 lần) ghi round, match, match event; set
  nhập/xóa → xóa cache asset.
- Khởi động: migrate → mở kênh chung → đóng trận dở → quét beatmapset → nhập file chờ trong `Data/Imports`. Nền: mỗi
  100 s đóng kết nối rảnh; watcher `Data/Imports`.

**Gói:** bỏ khỏi Infrastructure `dbup-sqlite`, `FFMpegCore`, `BouncyCastle` (theo Rijndael sang host),
`SixLabors.ImageSharp`, `Caching.Memory`, `Microsoft.Extensions.Http`, `Configuration.Binder`,
`Options.ConfigurationExtensions`, `Serilog.Sinks.*`, tham chiếu `Protocol.*`, `AllowUnsafeBlocks`/HardLink (sang
host). Gói ngoài còn lại: `Microsoft.Data.Sqlite`, `Dapper`, `BCrypt.Net-Next` (Storage); 4 ruleset ppy (Services);
ngoài ra chỉ `Microsoft.Extensions.*.Abstractions` và `Options`.

## BasilBot Application

Chỉ tham chiếu `Basil.Domain`. Cổng riêng (DTO riêng, sau này là đặc tả cho Host.Api): luồng event của bot (tin nhắn
tới, countdown, round bắt đầu/hủy, đổi cài đặt hủy countdown, phòng sắp đóng, anticheat), gửi tin kênh/PM, tra
user/quyền, beatmap theo id, FAQ, đọc phòng và mọi thao tác `!mp` thay mặt user (ủy quyền). Logic: phân tích lệnh
(tiền tố, ngoặc kép, `;`/`&&`), `!help/!roll/!where/!faq`, toàn bộ `!mp` (port `MpCommandService` ở `fc1bd033^`),
định tuyến trả lời (kênh/DM, chia 2000 ký tự), thông báo theo event (giữ trạng thái countdown trong bot), hằng
`BotReplies`/`MpReplies`.

## Thi công

Agent OpenCode `--standalone` trong git worktree riêng (build song song không đụng nhau), prompt đủ spec; tôi nối DI,
merge, review. Lỗi → Sonnet subagent → tôi.

| Pha | Việc | Model (req/5h) |
|---|---|---|
| P0 | Sửa Application/Domain + AGENTS.md | `qwen3.7-plus` (4 300) |
| P1 | 3 project Infrastructure + project bot, xóa code cũ, skeleton (DB, migration, pump) | `deepseek-v4.1-flash` (26 000) |
| P2a | Repo Users, Chat, Content | `deepseek-v4.1-flash` |
| P2b | Repo Multiplayer, Scores | `mimo-v2.6-flash` (30 100) |
| P2c | Repo Beatmap, Beatmapset + lọc tìm kiếm | `qwen3.7-plus` |
| P2d | File storage | `glm-5.3-flash` (6 320) |
| P2e | Reader + analyser | `hy3` (4 300) |
| P2f | Assets + preview + mirror | `minimax-m3` (3 200) |
| P2g | Runtime: handler, MatchRecorder, khởi động, vòng lặp, watcher | `gpt-6-luna` (4 230) |
| P3a/b | BasilBot: cổng + lõi / `!mp` | `qwen3.7-plus` / `minimax-m3` |
| P4 | Merge, DI, build, smoke, review, commit + push | tôi |

## Kiểm chứng

Build từng project; baseline Application `ALL PASS` sau P0; script smoke ngoài repo: migrate SQLite tạm, round-trip
mọi repository/storage, dựng DI thật, chạy đăng nhập → đóng kết nối, mở phòng → round → nộp điểm → đóng phòng rồi kiểm
dòng trong DB, nhập `.osz` mẫu; bot chạy lệnh với cổng giả. Review theo checklist AGENTS.md trước mỗi commit.
