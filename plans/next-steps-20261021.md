# Bước tiếp theo sau khi tái cấu trúc Application (2026-10-21)

Kế hoạch gốc: [`object-scope-plan-20261016.md`](object-scope-plan-20261016.md). Tài liệu này ghi lại điểm dừng, các
quyết định đã chốt, các câu hỏi còn mở và thứ tự làm tiếp. Việc làm tiếp (test, Host) chờ chủ dự án review xong.

## 1. Điểm dừng

Đã xong và đã đẩy lên `develop` (8 project build: `src/Domain`, `src/Application/{Storage,Objects,Services}`,
`src/Infrastructure/{Storage,Capabilities,Runtime}`, `src/Bot/Application`; kiểm DI 86 đăng ký không vòng; chưa có test nào chạy):

| Phần | Nội dung |
|---|---|
| Room, User, Lobby, Channels, Sessions, Beatmapset, Server | hành vi nằm trên đối tượng sở hữu, qua `ScopeTo(connection)` (xem [`object-scope-plan-20261016.md`](object-scope-plan-20261016.md)) |
| Channel | Domain chỉ còn `GeneralChannelRecord`/`GeneralChannelData`; Application có `Channel`, `GeneralChannel`, `RoomChannel`, `SpectatorChannel`, `PmChannel`; tên giữ dấu `#` |
| User | `UserSession` gộp vào `User`; một `User` sống mỗi lần online, tài khoản offline có bản tạm không vào registry |
| Phân tầng | cổng capability và `Paging` ở `Application.Storage`; `Infrastructure.Capabilities`; `Infrastructure.Storage` gom `Postgres/Files/Memory`; các service Users/Sessions/Room/Match/Chat/Score/Anticheat/Beatmapset đã chuyển vào đối tượng sở hữu (`UserRegistry`, `Lobby`, `Room`, `GeneralChannelRegistry`, `BanchoConnection`, `Beatmapsets`); Services chỉ còn `AccountService`, `BeatmapsetIndexer`, `ScoreRecorder` (xem [`application-layering-plan-20261021.md`](application-layering-plan-20261021.md) mục 7) |
| Ảnh và âm thanh | screenshot nhận dạng định dạng và kích thước bằng ImageSharp (trần 8192 px mỗi cạnh); preview audio đọc thời lượng và số byte bằng ffprobe (không giới hạn độ dài), tên file tạm luôn `.bin` |
| Server | `Server.Notify/NotifyAll` (thao tác hệ thống) và `ScopedServer.Notify/NotifyAll` (cần `ChatAnnounce`) gửi cho một, nhiều hay mọi client osu! |
| Quy ước | AGENTS.md có mục "Read the IDE's suggestions" (async + token, LINQ, return-switch, bỏ gợi ý cấp phát) để đưa vào skill sau |
| Tài liệu | AGENTS.md cập nhật theo phân tầng mới; `docs/` chưa cập nhật (chờ sau khi đảo tham chiếu) |

Bằng chứng kiểm tra: review diff từng dòng, build 8 project, kiểm DI (`ValidateOnBuild`, giải quyết toàn bộ đăng ký với cổng ngoài được stub).
**Chưa có test hành vi.** Thứ tự kiểm tra và kết quả của các luật đã port chỉ dựa trên review.

## 2. Quyết định đã chốt

* `RoomManageAny` (hiệu lực) hành động với quyền creator ở mọi room, kiểm trước `RoomJoin`; không có `RoomJoin` thì
  người khác không có quyền theo vai trò (`ScopedRoom`, `ChannelRules.InRoom` và `multiplayer.md` đã khớp).
* Player có `MembersInvite`; host được `RoundsAbort`.
* ImageSharp giữ v3 (v4 trả phí); cảnh báo SignalR/MessagePack bỏ qua (không dùng).
* Test và Host làm ở phiên sau, sau khi chủ dự án review.

## 3. Câu hỏi còn mở (chờ quyết)

1. **Cấp quyền theo quyền được cấp hay quyền hiệu lực.** `ScopedUserRegistry.CreateAccountAsync`, `ScopedUser.SetPermissions` và
   `Outranks` so quyền đã cấp (granted) của người thực hiện, đúng luật hierarchy ở AGENTS.md. Hệ quả: một người đang bị
   restriction vẫn cấp được cho tài khoản mới đúng những quyền họ đang bị treo. Đổi sang quyền hiệu lực thì phải đổi cả ba chỗ và
   hierarchy sẽ thay đổi theo restriction tạm thời. (Do bộ rà soát bảo mật nêu ra.)
2. **Registry thuộc Objects hay Storage.** Option A đặt `UserRegistry`/`GeneralChannelRegistry` ở `Application.Objects` vì chúng giữ
   đối tượng sống; chủ dự án từng nói "registry thuộc Storage". Cần xác nhận.
3. **Tên kết quả "quyền đang bị treo":** `RoomResult.Silenced` nhưng `UserResult.Restricted`, `BeatmapsetResult.Restricted`,
   `ChannelPostResult.Silenced`; AGENTS.md ghi `Restricted`. Đề xuất đổi (cần cập nhật `MpReplies`).
4. **`ConfigureAwait(false)`:** code mới không dùng; `BeatmapsetIndexer`, `Infrastructure.Runtime` và vài file Capabilities vẫn dùng.
   Đề xuất: cấm trong Application và Bot, cho phép trong Infrastructure.
5. **Kết thúc dòng và dòng cuối tệp:** `.editorconfig` đặt CRLF và không có dòng cuối, `.gitattributes` ép LF trong repo;
   Rider và agent đổi qua lại. Cần một quy ước một câu ghi vào AGENTS.md.
6. **Chia nhỏ AGENTS.md** (hơn 1000 dòng): giữ luật cốt lõi, đưa mục chi tiết sang `docs/for-agents/` kèm liên kết.
7. **Lý do huỷ countdown:** bot chỉ nói "Countdown aborted." vì room không nêu lý do. Có cần event/giá trị riêng?
8. **Liên kết `ModerationRecord` với `Restriction`:** (A) cột `restriction_id`, hay (B) `ModerationRecord` trừu tượng với các kiểu con bất biến
   (đề xuất B). Chưa chọn.
9. **Getter `Visible`/`Locked` trên `ScopedBeatmapset`:** đề xuất chỉ getter, không setter.
10. **Giữ tên bot khi đổi tên:** chưa có luật chặn người dùng chiếm tên "BasilBot" sau khi bot đổi tên.

## 4. Thứ tự làm tiếp (đề xuất)

1. **Đảo tham chiếu** (xem mục 7 của [`application-layering-plan-20261021.md`](application-layering-plan-20261021.md)); sau đó cập nhật
   `docs/for-developers/architecture.md` và các tài liệu liên quan. **Đang chờ lệnh chủ dự án.**
2. **Chủ dự án review** các thay đổi (chuỗi commit từ `3dcdbaef` đến hiện tại) và trả lời mục 3.
3. **Migrate test** trước Host, vì test cho biết ngay đợt viết lại có sai hành vi không:
   * cập nhật namespace/cấu trúc từng project test; `Basil.ArchitectureTests` theo cấu trúc mới (ba project Application,
     Infrastructure chia Storage/Capabilities/Runtime, luật "Infrastructure không tham chiếu Services", "Services không có kiểu public");
   * viết test hợp đồng cho các luật mới: bảng quyền room theo vai trò và `RoomManageAny` trước `RoomJoin`; hiệu ứng restriction
     (đẩy khỏi room, gỡ referee kể cả offline, dừng spectate); bảo vệ BasilBot; `DeletedAt` không về null; block; host đóng thì gỡ
     spectator; `BotConnection` không thể xin hay đăng nhập; `CommandWorker`; countdown; truy vấn `filename` và leaderboard;
     định danh theo tham chiếu của `User` online và bản tạm offline; screenshot (định dạng, trần kích thước); preview audio;
   * giữ nguyên quy tắc AGENTS.md mục 8 (test hợp đồng quan sát được, không test cài đặt).
4. **Migrate Host:** gói tin, route, SSE, GeoIP; gọi `AddApplication()`; hiện bot là user online (bỏ qua `BotConnection` khi giao tin);
   giao tin kênh chỉ cho người trong `Channel.RecipientsAsync`; `osu-getfriends.php`, `osu-getbeatmapinfo.php` (tiêu chí `filename`),
   `osu-osz2-getscores.php` (`ScoreQuery`); upload screenshot map `ScreenshotUploadFailure.DimensionsTooLarge`; API room, timer,
   moderation, friends, thông báo (`Notify`).
5. **`Basil.Cli`:** console của operator (tạo tài khoản quản trị đầu tiên, chạy lệnh với BasilBot làm tác nhân, bỏ qua kiểm quyền).
6. **Việc nhỏ còn lại:** thu gọn setter `internal` của `RoomSlot`; kiểm chứng kết quả SQL mới bằng dữ liệu thật; xoá tay các thư mục
   worktree bị khoá (`osuBasil-b2`, `osuBasil-wt-img`, `osuBasil-wt-store`, `osuBasil-wt-bm`, `osuBasil-wt-score`).

## 5. Cách làm việc với agent (rút từ đợt này)

* Spec đầy đủ trong thư mục tạm, bắt đầu bằng yêu cầu dùng Rider MCP, CodeGraph, Context7/Microsoft Learn.
* `opencode-go/gpt-6-luna`; tối đa hai agent cùng lúc (RAM); agent thứ hai làm trong git worktree riêng, tôi gộp commit.
* Review từng dòng; những thứ agent hay để lại được liệt kê ở mục 8 của "Final verification" trong AGENTS.md.
* Rider MCP chỉ thấy checkout chính: agent trong worktree dùng `git mv`, tìm và thay bằng shell, `dotnet build`; tôi chạy kiểm DI sau khi gộp.
* Hai agent cùng lúc là được khi chúng không đụng cùng file (một ở thư mục chính, một trong worktree); xung đột còn lại thường chỉ là
  `DependencyInjection.cs`.
* Chỉ `git add` đúng đường dẫn; `git add -A` cả cây đã từng gom file nhận được từ chỗ khác vào commit.
* Agent hay biến `switch` thành switch-expression kèm `if` (khó đọc hơn) và thêm `.ConfigureAwait(false)`: xem mục 8 của "Final
  verification" trong AGENTS.md.
