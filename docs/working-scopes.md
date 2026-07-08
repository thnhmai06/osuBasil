# Phạm vi hoạt động

**Basil** là một server giải đấu multiplayer offline, được xây dựng dựa trên nền móng của **bancho.py**, không phải một server osu! hoàn chỉnh.

Trang này liệt kê những gì *hiện có* trong phạm vi đó, những gì không có và lý do tại sao lại loại bỏ.

## Lệnh chat

Dispatch bởi `ICommandDispatcher`/`CommandDispatcher` (lệnh chung) và `MpCommandService` (`!mp` subcommand), cả hai dưới `UseCases/Bot/`. Danh sách đầy đủ lệnh, cách dùng, và so sánh từng lệnh với bancho.py giờ nằm ở [`bot-commands.md`](bot-commands.md) — không lặp lại bảng ở đây để tránh 2 nguồn lệch nhau theo thời gian.

## Ngoài phạm vi hiện tại

| Hạng mục | Mô tả | Lý do |
| --- | --- | --- |
| ❌ `!pool` + `!mp loadpool/unloadpool/ban/unban/pick` | Toàn bộ hệ thống mappool cho giải đấu (7 lệnh `!pool`, 4 subcommand `!mp` liên quan) | Cần lớp persistence mới (`tourney_pools`/`tourney_pool_maps`) chưa tồn tại |
| ❌ Scrim engine (`!mp scrim`/`autoref`/`endscrim`/`rematch`) | Race-safe match-point tallying cho scrim tự động trọng tài | Engine gốc (`MatchScoringService`) đã xoá hẳn cùng lớp lệnh cũ; chưa được yêu cầu xây lại |
| ❌ `!mp force` | Admin ép người chơi vào trận | Chưa triển khai |
| ❌ `!block`/`!unblock`/`!reconnect`/`!changename`/`!apikey` | Lệnh chat cá nhân (chặn user, đổi tên, quản lý API key...) | Nằm ngoài phạm vi multiplayer/giải đấu |
| ❌ Friends (`osu-getfriends.php`, `FriendAddHandler`/`FriendRemoveHandler`) | HTTP endpoint + packet handler cho quan hệ bạn bè | Tính năng xã hội, ngoài phạm vi multiplayer/giải đấu |
| ❌ Public JSON API v1/v2 tổng quát | API REST công khai kiểu osu-web (OAuth, rate limiting, versioning) cho công cụ bên ngoài | Chưa có nhu cầu cụ thể; người dùng dự định tự xây riêng sau |
| ❌ Moderation/clan/metrics (`!clan`, admin command, webhook Discord, Datadog) | Lệnh clan/moderator, audit-log webhook, metric `bancho.online_players`/`bancho.login_time` | Phụ thuộc lớp lệnh đã xoá; dự án không dùng Datadog nên không có gì để port |
| ❌ Trang debug HTML (`/matches`, `/online`) | View debug hướng admin của Bancho | Không bao giờ được game client gọi, từ chối rõ ràng |
| ⚠️ `osu-screenshot.php` | Upload screenshot | Stub - trả 400 "not available" |
| ⚠️ `osu-getfavourites.php`/`osu-addfavourite.php` | Danh sách map yêu thích | Stub - trả rỗng |
| ⚠️ `osu-rate.php` | Đánh giá sao cho map | Stub - trả `"not ranked"` (mã phản hồi thật của Bancho) |
| ⚠️ `osu-comment.php` | Bình luận trong game | Stub - trả rỗng |
| ⚠️ `POST /users` (đăng ký trong game) | Tạo tài khoản qua game client | Stub - trả lỗi "registration disallowed" thật của Bancho |
| ❌ Tính pp | Performance points cho scoring/leaderboard/điều kiện thắng | Cố tình không có - star rating chỉ để hiển thị, tính qua ruleset osu!lazer của ppy |

## Bài học từ các quyết định bị đảo ngược

| Quyết định cũ | Vấn đề | Hiện tại |
| --- | --- | --- |
| Xoá hẳn `BanchoBot` + toàn bộ lớp lệnh chat (kể cả `!mp` đầy đủ 25 lệnh con) khi pivot sang "chỉ multiplayer + giải đấu" | Giải đấu vẫn cần cách điều khiển trận qua chat (`!mp start`, đổi map, quản slot...) - xoá sạch rồi mới nhận ra cần lại | `BanchoBot` bootstrap lại thành session thật (`BotBootstrapService`); lớp dispatch **hoàn toàn mới** (`ICommandDispatcher`/`CommandDispatcher`/`MpCommandService`) - cố tình không hồi sinh `ICommand`/`MpCommandDispatcher` cũ, chỉ wrap lại các mutation `MatchSession`/`MatchMembershipService` đã có sẵn, phạm vi hẹp hơn bộ lệnh gốc |
| Hoãn vô thời hạn "API v1/v2 làm sau" | Giải đấu cần theo dõi trận trực tiếp (report, WebSocket) dù chưa cần API public đầy đủ | Host `api.<domain>` được xây cho tournament match report (TRT) qua `GET`/WebSocket, tải replay/beatmap, và management CRUD khoá admin-key - hẹp hơn nhiều so với API v1/v2 công khai (không OAuth, không rate limit, không versioning), API tổng quát đó vẫn chưa xây |
| Kế hoạch test parity tự động ("chạy song song Bancho và Basil, so kết quả") | Không còn hợp lý khi phần lớn bề mặt tính năng Bancho đã bị cắt có chủ đích - không có gì để so sánh song song nữa | Test thủ công một luồng multiplayer/giải đấu thật bằng hai client osu! thật - xem [`getting-started.md`](getting-started.md) |
