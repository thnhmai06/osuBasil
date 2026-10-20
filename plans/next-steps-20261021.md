# Bước tiếp theo sau khi tái cấu trúc Application (2026-10-21)

Kế hoạch gốc: [`object-scope-plan-20261016.md`](object-scope-plan-20261016.md). Tài liệu này ghi lại điểm dừng, các
quyết định đã chốt, các câu hỏi còn mở và thứ tự làm tiếp. Việc làm tiếp (test, Host) chờ chủ dự án review xong.

## 1. Điểm dừng

Đã xong và đã đẩy lên `develop` (build 6 project: `src/Domain`, `src/Application/Services`,
`src/Infrastructure/{Storage,Services,Runtime}`, `src/Bot/Application`; chưa có test nào chạy):

| Phần | Nội dung |
|---|---|
| Room | slots, settings, authority, rounds, countdown, members, đóng room, qua `room.ScopeTo(by)` |
| User | `users.Of(record).ScopeTo(by)`: permission, xoá, restriction (+ log moderation), friend/block, lịch sử login, thu hồi connection, đổi password; BasilBot được bảo vệ; `DeletedAt` vĩnh viễn |
| Lobby | `lobby.ScopeTo(by)`: mở room, watch/unwatch; lobby tự đóng room và giữ timer room trống |
| Channels | `channel.ScopeTo(by)`: join, part, post; channel general: cấu hình, xoá; `RecipientsAsync` lọc im lặng theo block |
| Sessions | spectate, status trên `BanchoConnection`; away/PM-private trên `UserSession`; host đóng thì gỡ spectator |
| Beatmapset | xoá, ẩn, khoá qua `beatmapsets.Of(record).ScopeTo(by)` |
| Server | object gốc: announce, tạo channel general, screenshot; thao tác hệ thống `OpenChannel`, `SayAsync`, `WhisperAsync`, `FindChannel` |
| BasilBot | handler sự kiện (`IEventHandler` nằm trong Contracts), lệnh chạy trên connection của người gõ, `!mp` trên room đã scope, `BotConnection` (online), tin riêng tới bot là lệnh, hàng đợi lệnh có giới hạn |
| Dữ liệu | tiêu chí `filename` của `BeatmapQuery`, `ScoreQuery` cho leaderboard (Mods, Players, BestPerPlayer) |
| Tài liệu | AGENTS.md, architecture, working-scopes, multiplayer, privileges, chat, basil-bot |

Bằng chứng kiểm tra: review diff từng dòng, build 6 project, kiểm tra DI (`ValidateOnBuild`, giải quyết toàn bộ 96
đăng ký với port lưu trữ được stub), chạy thử các câu SQL mới trên DB dev (đúng cú pháp với schema thật; các bảng đang
rỗng nên chưa kiểm chứng kết quả). **Chưa có test hành vi.** Thứ tự kiểm tra và kết quả của các luật đã port chỉ dựa trên
review.

## 2. Quyết định đã chốt

* `RoomManageAny` (hiệu lực) hành động với quyền creator ở mọi room, kiểm trước `RoomJoin`; không có `RoomJoin` thì
  người khác không có quyền theo vai trò (`ScopedRoom`, `ChannelRules.InRoom` và `multiplayer.md` đã khớp).
* Player có `MembersInvite`; host được `RoundsAbort`.
* ImageSharp giữ v3 (v4 trả phí); cảnh báo SignalR/MessagePack bỏ qua (không dùng).
* Test và Host làm ở phiên sau, sau khi chủ dự án review.

## 3. Câu hỏi còn mở (chờ quyết)

1. **Tên kết quả "quyền đang bị treo":** `RoomResult.Silenced` nhưng `UserResult.Restricted`,
   `BeatmapsetResult.Restricted`; AGENTS.md ghi `Restricted`. Đề xuất đổi `Silenced` thành `Restricted` ở `RoomResult`
   (đổi tên công khai, cần cập nhật `MpReplies`).
2. **`ConfigureAwait(false)`:** code mới không dùng, `BeatmapsetService` và `Infrastructure.Runtime` vẫn dùng. Đề xuất
   chính sách: cấm trong Application và Bot, cho phép trong Infrastructure.
3. **Kết thúc dòng và dòng cuối tệp:** `.editorconfig` đặt CRLF và không có dòng cuối, `.gitattributes` ép LF trong
   repo; Rider và agent đổi qua lại. Cần một quy ước một câu ghi vào AGENTS.md.
4. **Chia nhỏ AGENTS.md** (hơn 1000 dòng): giữ luật cốt lõi, đưa các mục chi tiết (quy ước repository và bộ nhớ, tài sản,
   bot và console) sang `docs/for-agents/` kèm liên kết. Chỉ làm khi chủ dự án đồng ý.
5. **Lý do huỷ countdown:** khi settings đổi làm huỷ countdown, bot chỉ nói "Countdown aborted." vì room không nêu lý do.
   Có cần event/giá trị riêng cho lý do không?

## 4. Thứ tự làm tiếp (đề xuất)

1. **Chủ dự án review** các thay đổi (chuỗi commit từ `3dcdbaef` đến hiện tại).
2. **Migrate test** trước Host, vì test cho biết ngay đợt viết lại có sai hành vi không:
   * cập nhật namespace/cấu trúc từng project test; `Basil.ArchitectureTests` theo cấu trúc mới
     (hai project Application, không còn Storage.Implementations);
   * viết test hợp đồng cho các luật mới: bảng quyền room theo vai trò và `RoomManageAny` trước `RoomJoin`; hiệu ứng
     restriction (đẩy khỏi room, gỡ referee kể cả offline, dừng spectate); bảo vệ BasilBot; `DeletedAt` không về null và
     `CopyTo`; block (invite, spectate, PM, `RecipientsAsync`); host đóng thì gỡ spectator; `BotConnection` không thể xin hay
     đăng nhập; `CommandWorker` (giới hạn hàng đợi, theo người, timeout); countdown (đủ mốc, huỷ khi abort/đổi settings);
     truy vấn `filename` và leaderboard với dữ liệu thật;
   * giữ nguyên quy tắc AGENTS.md mục 8 (test hợp đồng quan sát được, không test cài đặt).
3. **Migrate Host:** gói tin, route, SSE, GeoIP; hiện bot là user online (nhưng bỏ qua `BotConnection` khi giao tin);
   giao tin kênh chỉ cho người trong `ChannelSession.RecipientsAsync`; `osu-getfriends.php`, `osu-getbeatmapinfo.php`
   (dùng tiêu chí `filename`), `osu-osz2-getscores.php` (dùng `ScoreQuery` mới); API room, timer, moderation, friends.
4. **`Basil.Cli`:** console của operator (tạo tài khoản quản trị đầu tiên, chạy lệnh với BasilBot làm tác nhân, bỏ qua
   kiểm quyền).
5. **Việc nhỏ còn lại:** thu gọn setter `internal` của `RoomSlot`; `SessionConnectionsService` (Close, MarkActive,
   CloseIdle) có thể vẫn là service điều phối, ghi quyết định này vào AGENTS.md khi test xong; kiểm chứng kết quả SQL mới
   bằng dữ liệu thật; xoá thủ công thư mục rỗng `V:\Code\cs\osuBasil-b2` nếu còn.

## 5. Cách làm việc với agent (rút từ đợt này)

* Spec đầy đủ trong thư mục tạm, bắt đầu bằng yêu cầu dùng Rider MCP, CodeGraph, Context7/Microsoft Learn.
* `opencode-go/gpt-6-luna`; tối đa hai agent cùng lúc (RAM); agent thứ hai làm trong git worktree riêng, tôi gộp commit.
* Review từng dòng; những thứ agent hay để lại được liệt kê ở mục 8 của "Final verification" trong AGENTS.md.
