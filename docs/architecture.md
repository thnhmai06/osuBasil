# Kiến trúc

## Kiến trúc hệ thống

**Basil** tuân thủ kiến trúc **Monolith Clean Architecture** - một bản deploy duy nhất, nhưng có kỷ luật dependency-inversion của Clean Architecture được enforce giữa các layer.

Rule này được kiểm tra bởi bộ test tự động (`tests/Basil.ArchitectureTests`, dùng [NetArchTest](https://github.com/BenMorris/NetArchTest)), không chỉ để lại như một quy ước - một PR vi phạm hướng phụ thuộc sẽ fail CI.

![Clean Architecture](docs\assets\clean-architecture.jpg)

| Project                | References                            | Mục đích                                                                                   |
| ---------------------- | ------------------------------------- | ------------------------------------------------------------------------------------------ |
| `Basil.Domain`         | Không có                              | C# thuần: enum, record, value calculator                                                   |
| `Basil.Protocol`       | Không có                              | Đọc/ghi packet định dạng wire của bancho                                                   |
| `Basil.Application`    | Domain, Protocol                      | Use case, packet handler, và*port* (interface) mô tả những gì Infrastructure phải cung cấp |
| `Basil.Infrastructure` | Application, Domain                   | Triển khai SQLite/filesystem/thư viện ruleset osu!lazer                                    |
| `Basil.Web`            | Application, Infrastructure, Protocol | Host ASP.NET Core: routing theo subdomain, composition root, Program.cs                    |

**Dependency rule:**

- **Domain và Protocol không phụ thuộc gì khác trong solution.**
- Application phụ thuộc Domain và Protocol, nhưng không bao giờ phụ thuộc Infrastructure hay Web.
- Infrastructure triển khai các interface đó nhưng không bao giờ được Application reference.
- Web là project duy nhất được phép biết về cả bốn project còn lại - nó là composition root nối interface với triển khai cụ thể của chúng khi khởi động.

điều này nghĩa là:

- Mọi chi tiết SQLite/filesystem đều nằm trong `Basil.Infrastructure`
- Use case của `Basil.Application` có thể được unit test bằng cách thay thế fake cho các interface đó - không cần database.
- `tests/Basil.Infrastructure.Tests` là bộ test duy nhất nói chuyện với SQLite thật (một file tạm dựng lên mỗi lần chạy test, xoá khi xong), xác minh các triển khai cụ thể thực sự khớp với schema.

## Bố cục Layer

Ba thư mục lớn nhất được tổ chức theo khu vực tính năng thay vì để phẳng:

| Thư mục | Mục đích |
| --- | --- |
| **`PacketHandlers/`** | Một class cho mỗi bancho client packet, chia thành `Core/` (vòng đời session: các packet liên quan login, presence, stats), `Channels/` (chat), `Spectating/`, và `Multiplayer/` (packet match + tournament, nhóm lớn nhất) |
| **`Abstractions/`** | Các port mà Infrastructure triển khai, chia theo khái niệm domain: `Beatmaps/`, `Scores/`, `Users/`, `Channels/`, `Social/` (mail + relationship + moderation logging) |
| **`Sessions/`** | Trạng thái session trong bộ nhớ (`PlayerSession`, `ChannelSession`, `MatchSession`) và các registry theo dõi nó, chia thành thư mục con `Channels/`, `Irc/` (IIrcConnection — bridge cho bancho packet hay real TCP), và `Multiplayer/` với trạng thái cấp player ở gốc. `Sessions/Multiplayer/IMatchEventBus` là pub/sub non-blocking dùng để đẩy trạng thái match trực tiếp tới lớp WebSocket của host `api.` |
| **`UseCases/`** | Một thư mục cho mỗi tính năng (`Authentication/`, `Beatmaps/`, `Multiplayer/`, `Scores/`, `Spectating/`, `Mail/`, `Anticheat/`, `Bot/`, `Chat/`, `Irc/`), mỗi thư mục chứa business logic thực sự mà một packet handler hay HTTP route ủy quyền tới. `UseCases/Chat/ChatDispatchService` là entry point duy nhất cho mọi traffic chat — dùng bởi cả bancho handler lẫn IRC PRIVMSG. `UseCases/Irc/IrcAuthenticationService` xác thực kết nối IRC TCP và tạo PlayerSession ảo. `UseCases/Multiplayer/MatchReportService` xây dựng tournament match report (TRT) tại thời điểm đọc. `UseCases/Bot/` là bootstrap session của BanchoBot cộng với command dispatcher `!help`/`!roll`/`!mp` |

`Basil.Domain`, `Basil.Protocol`, và `Basil.Infrastructure/Persistence` theo cùng pattern (thư mục con theo chủ đề như `Login/`, `Beatmaps/`, `Scores/`, `Multiplayer/`, `Users/`, `Repositories/`) - namespace khớp với đường dẫn thư mục, nên `grep` một import sẽ cho biết chính xác file nằm ở đâu.

## Luồng request

### Login

Client osu! gửi login dưới dạng một HTTP POST không có header `osu-token`. Không có packet riêng cho việc này.

1. `Basil.Web/Routing/BanchoHostGroups.cs` - route `POST /` của nhóm subdomain `c.`/`ce.`/`c4.`/`c5.`/`c6.` đọc raw body, resolve IP client (`Basil.Domain.Login.ClientIpResolver`), và gọi `OsuLoginUseCase.ExecuteAsync`.
2. `OsuLoginUseCase` (`Basil.Application/UseCases/Authentication/`) parse body login (`LoginDataParser`, `OsuVersionParser`, `AdaptersStringParser` - tất cả trong `Basil.Domain.Login`), xác thực qua `IUserRepository`/`IPasswordHasher`, kiểm tra session hiện có, load stats theo từng mode qua `IStatsRepository`, và xây dựng một `PlayerSession`.
3. Các triển khai cụ thể của `IUserRepository`/`IStatsRepository`/`IPasswordHasher` (`SqliteUserRepository`, `SqliteStatsRepository`, `BCryptPasswordHasher`) nằm trong `Basil.Infrastructure` và được nối vào lúc khởi động bởi `InfrastructureServiceCollectionExtensions`/`ApplicationServiceCollectionExtensions` - bản thân `OsuLoginUseCase` không bao giờ reference một class cụ thể, chỉ reference interface.
4. Response là một stream các packet bancho (`Basil.Protocol.Packets.ServerPacketWriter`) - phiên bản giao thức, phản hồi login, privileges, danh sách channel, và presence/stats đã cache của mọi player đang online.

Mọi request tiếp theo của client đều mang header `osu-token`; `BanchoHostGroups.cs` tra session theo token và dispatch body packet qua `BanchoPacketDispatcher` tới handler tương ứng trong `PacketHandlers/`.

### Multiplayer

1. Client gửi packet `CREATE_MATCH` → `BanchoPacketDispatcher` route nó tới `CreateMatchHandler` (`PacketHandlers/Multiplayer/`).
2. `CreateMatchHandler` ủy quyền cho `MatchMembershipService.Create` (`UseCases/Multiplayer/`), hàm này cấp phát nguyên tử một match ID từ `IMatchRegistry` 64 slot, xây dựng một `MatchSession` (`Sessions/Multiplayer/`), đăng ký chat channel riêng của nó, và cho host vào slot 0.
3. Mọi packet handler match tiếp theo (`MatchChangeSlotHandler`, `MatchReadyHandler`, `MatchStartHandler`, v.v.) đều acquire `MatchSession.Lock` - một `SemaphoreSlim(1, 1)` cho từng match - trước khi đọc hoặc mutate trạng thái slot, sau đó broadcast trạng thái match đã cập nhật trước khi release nó. 
   
   > [!NOTE]
   > Lock này là bổ sung riêng cho **Basil**: mã nguồn Python gốc của **Akatsuki** dựa vào event loop đơn luồng của asyncio để đảm bảo tính nguyên tử giữa các điểm `await`, điều mà thread pool thật của ASP.NET Core không cho miễn phí.
4. `tests/Basil.Application.Tests/Sessions/MatchSessionRaceTests.cs` là test thực sự chứng minh lock hoạt động - nó tái tạo một race lost-write có thật khi bỏ lock, rồi cho thấy cùng kịch bản đó không còn race khi có lock.

## Database Schema

*Schema dùng tên bảng/cột tuân thủ PascalCase, id auto-increment bắt đầu từ 1 (không có khoảng trống id kiểu của Akatsuki).*

Các bảng phục vụ cho việc **quản lý chung**:

| Bảng | Mục đích |
| --- | --- |
| `Users` | Tài khoản: credential, priv, clan/mode mặc định. `Id = 1` seed sẵn cho `BanchoBot` |
| `Mapsets` | Một hàng cho mỗi beatmapset - chỉ để `Beatmaps.SetId` tham chiếu, không track staleness osu!api (server chạy offline, mapset chỉ được thêm qua ingest cục bộ) |
| `Beatmaps` | Một hàng cho mỗi difficulty, khoá bởi `Md5`; nguồn cho việc tra map khi submit score và hiển thị star rating tính local |
| `Channels` | Danh mục chat channel tĩnh (`#osu`, `#announce`, `#lobby`, ...) cộng cờ `ReadPriv`/`WritePriv`/`AutoJoin` |
| `Mail` | Tin nhắn offline giữa hai user (gửi khi người nhận không online) |
| `Relationships` | Cặp `(User1, User2)` kiểu `friend`/`block` |
| `Ratings` | Đánh giá sao 1-10 của user cho một map (`UserId`, `MapMd5`), dùng bởi `BeatmapLeaderboardService` |
| `ClientHashes` | Log hardware fingerprint (adapters/uninstall id/disk serial) mỗi lần login - chỉ dùng cho anticheat, không có logic chặn tự động |
| `IngameLogins` | Log mỗi lần login: IP, version client, stream - chỉ ghi, không có consumer đọc lại |
| `Logs` | Log hành động chung `From`/`To`/`Action` (vd. moderation) - chỉ ghi |

Các bảng quan trọng cho **luồng giải đấu**:

| Bảng | Mục đích |
| --- | --- |
| `Matches` | Một hàng cho mỗi phòng multiplayer. `Id` là id ổn định mà consumer bên ngoài dùng - khác với `MatchSession.Id`, slot trong bộ nhớ 0-63 mà bản thân giao thức wire của bancho dùng |
| `Rounds` | Một hàng cho mỗi beatmap được chơi trong một match, tạo tại `MATCH_START`/`!mp start` |
| `Scores` | Liên kết tới một `Round` qua `RoundId`, nộp qua pipeline `osu-submit-modular-selector.php` sẵn có |
| `UserStats` | Được seed một lần ở giá trị 0 và không bao giờ được score submission cập nhật - server này không có xếp hạng/tiến triển singleplayer, nên stats theo từng mode là dữ liệu cố định chỉ để hiển thị, không tính toán trực tiếp |

> [!IMPORTANT]
>**Việc liên kết score-tới-round không có race window theo thiết kế** 
> 
>`MatchMembershipService.StartAsync` tạo hàng `Round` và lưu id của nó vào `MatchSession.CurrentRoundId` _trước khi_ gameplay bắt đầu. Score submission (một HTTP request) và `MATCH_COMPLETE` (một packet bancho) đến trên hai kết nối không liên quan, không có đảm bảo thứ tự giữa chúng - nên score submission đọc `CurrentRoundId` trực tiếp tại thời điểm submit thay vì có bước "gom score sau khi match hoàn thành" phải chờ cả hai.

## Tournament match report (TRT)

**TRT không bao giờ được lưu trữ trên Database** - `MatchReportService` (`UseCases/Multiplayer/`) xây dựng nó khi đọc từ `Matches`/`Rounds`/`Scores` (một match đã kết thúc) hợp nhất với `MatchSession` trực tiếp (một match đang diễn ra, tra bằng `IMatchRegistry.GetByDbId`). 

`WinningTeam` cũng được tính khi đọc, không lưu trữ: nhóm các `Scores` đã hoàn thành theo team nếu có team không trung lập, nếu không thì rơi về người có điểm cao nhất cá nhân.

Cập nhật trực tiếp được đẩy qua ba kênh WebSocket ASP.NET Core thô dưới host `api.`:

| Endpoint | Mục đích |
| --- | --- |
| `WS /multi/{id}` | Trạng thái toàn match (slot/map/status), publish từ `MatchMembershipService.EnqueueState` - điểm nghẽn cổ chai duy nhất mà mọi packet match thay đổi trạng thái đã route qua sẵn |
| `WS /multi/{id}/{playerName}` | Điểm trực tiếp của một player, publish từ `MatchScoreUpdateHandler` sau khi decode score frame |
| `WS /multi/{id}/input` | Raw spectator input frame, publish từ `SpectateFramesHandler` chỉ khi có ai đó đang spectate một player trong match đó |

Cả ba đều publish qua `IMatchEventBus`, có các method `Publish*` là `ChannelWriter.TryWrite` non-blocking - an toàn để gọi từ code vẫn đang giữ `MatchSession.Lock` (như `EnqueueState` và các handler score/spectate đang làm), vì việc ghi socket thực sự xảy ra trên một task pump riêng cho từng connection, tách biệt hoàn toàn, không inline với lời gọi publish.

`GET /multi/{id}` (không upgrade) trả về cùng report đó dưới dạng một snapshot JSON one-shot thay vì một stream WS.

## IRC Gateway

Basil chạy một **IRC gateway embedded** (không executable riêng, không Docker) — bất kỳ IRC client thật nào (hay tool tournament như osu-ahr) cũng có thể kết nối TCP tới port 6667 và chat/`!mp` cùng các osu! client.

Mỗi `PlayerSession` có một `IIrcConnection` (`Sessions/Irc/`):

| Implementation | Dùng cho | Hành vi |
|---|---|---|
| `BanchoIrcBridgeConnection` | Mặc định — mọi osu! client login bình thường | `Send(IrcMessage)` chỉ phản hồi `PRIVMSG`, encode lại thành packet `SEND_MESSAGE` bancho cho session poll |
| `TcpIrcConnection` | IRC client thật qua TCP socket | Chạy read-loop (PASS/NICK/USER → PRIVMSG/JOIN/PART/AWAY/PING/QUIT), write-pump non-blocking qua bounded channel (DropOldest), ping loop 60s |

**Chat core thống nhất:** Mọi chat — từ osu! client (SendPublicMessage/SendPrivateMessage handler) hay từ IRC PRIVMSG — đều qua `ChatDispatchService.SendPrivmsgAsync`. Lớp này quyết định:

1. Kênh (`#` prefix): broadcast qua `ChannelMembershipService.BroadcastPrivmsg` (gửi tới từng member's IIrcConnection), rồi chạy `ICommandDispatcher` cho lệnh `!`.
2. Bot DM: gửi thẳng tới `ICommandDispatcher` (prefix không bắt buộc).
3. DM thường: check block/silence, deliver qua `target.IrcConnection.Send`, lưu offline mail.

`BanchoIrcBridgeConnection.Send` lọc mọi IRC command trừ `PRIVMSG` — bancho client không cần JOIN/PART/QUIT numerics (channel presence đã có qua ChannelInfo packet). IRC client thật nhận được tất cả.

### Luồng login IRC

1. `TcpIrcListener` (`Infrastructure/Irc/`, `BackgroundService`) accept TCP connection trên port config (`IrcOptions.Port`, mặc định 6667).
2. `TcpIrcConnection.ReadLoopAsync` đọc PASS + NICK + USER. Khi có cả nick và pass, gọi `IrcAuthenticationService.AuthenticateAsync`.
3. `IrcAuthenticationService` tra `IUserRepository.FetchByNameAsync`, lấy password hash qua `FetchPasswordHashAsync`, **MD5 plaintext PASS rồi bcrypt verify** (giống hệt osu! client login flow). Tạo `PlayerSession` ảo (không bancho socket) với `IrcConnection = chính TcpIrcConnection đó`, join auto-join channels, trả về numerics RplWelcome + RplTopic + RplNamReply.
4. Sau registered, mọi `PRIVMSG`/`JOIN`/`PART`/`AWAY`/`QUIT` từ IRC client được `TcpIrcConnection` dispatch tới `ChatDispatchService`/`ChannelMembershipService`.

> [!NOTE]
> **Passwords:** IRC PASS yêu cầu **mật khẩu account** (giống login osu! client) — khác với osu!Bancho chính thức (irc.ppy.sh dùng password riêng "different from your account password"). `ApiKey` và `UpdateApiKeyAsync` trong `IUserRepository` từng tồn tại ở dạng staged nhưng là dead code, đã được xoá trước khi commit.

## Handler cho BanchoBot

`BanchoBot` (`UseCases/Bot/BotBootstrapService`) được bootstrap như một `PlayerSession` thật khi khởi động - không có kết nối client nào phía sau nó, nên nó được miễn khỏi đợt reap của `GhostDisconnectService` qua `PlayerSession.IsBot` (nó không bao giờ gửi packet ping thật, nên `LastRecvTime` sẽ không bao giờ tiến nếu không có ngoại lệ này). 

`SendPublicMessageHandler`/`SendPrivateMessageHandler` chuyển mọi message tới `ChatDispatchService.SendPrivmsgAsync`, lớp này route message bắt đầu bằng `!` cho `ICommandDispatcher` — dispatcher route tới hoặc bảng lệnh thông thường (`!help`, `!roll`) hoặc `MpCommandService` cho `!mp <subcommand>` - cái sau chỉ khi message được gửi trong chat channel của chính match người gửi, và được kiểm soát bởi `MatchSession.IsReferee`.

Reply của bot được broadcast qua `ChannelMembershipService.BroadcastPrivmsg` (IRC-shaped) thay vì build packet trực tiếp — nên real IRC client trong channel cũng thấy câu trả lời của BasilBot.

## TL;DR

Khi thêm một tính năng mới, hãy nhớ:

| Tính năng | Cách thêm |
| --- | --- |
| Một packet bancho mới | Một class mới trong thư mục con `PacketHandlers/*` tương ứng, đăng ký trong `ApplicationServiceCollectionExtensions`, đếm trong `CompositionRootTests` |
| Một mảnh trạng thái được lưu trữ mới | Một method mới trên một interface hiện có (hoặc mới) dưới `Abstractions/*`, triển khai trong `Basil.Infrastructure/Persistence/Repositories/` |
| Một HTTP endpoint mới | Một route mới trong `Basil.Web/Routing/BanchoHostGroups.cs`, dưới host group tương ứng (`osu.`, `b.`, `api.`) mà nó thuộc về |
| Một IRC command mới | Dispatch trong `TcpIrcConnection.HandleRegisteredCommandAsync` (`Infrastructure/Irc/`), gọi service có sẵn ở Application layer |
| Chat routing mới | Logic thêm vào `ChatDispatchService.SendPrivmsgAsync` (`UseCases/Chat/`) — entry point duy nhất cho mọi chat |
| Transport chat mới (không phải bancho packet, không phải IRC TCP) | Implement `IIrcConnection` interface (`Application/Sessions/Irc/`) — lớp mới nhận `IrcMessage` và encode ra format tương ứng |
