# Kế hoạch: Identity thống nhất, Permissions theo nghiệp vụ, BasilBot là client ngoài

Plan này (`plans/identity-permissions-plan-20261005.md`, đã duyệt 2026-10-05) là nguồn sự thật cho
các layer sau.

## Context

Basil đang coi BasilBot là thực thể đặc biệt của server:
* user id `0` cố định (`SystemUserIds.BasilBot`);
* `BotConnection`/`ConnectionType.Bot`, mở bởi `ISessionService.OpenBotAsync()`;
* `by is BotConnection` qua mọi kiểm tra phòng (`RoomRules`) và kênh (`ChannelService`): đọc mọi kênh phòng, bỏ qua
  chặn PM, không bị kick/ban/referee/silence/xóa;
* API (khóa admin, bypass khi chưa đặt) hành động với tư cách bot hoặc `null`; phòng tạo qua API có `Creator = null`.

Quyền đang lưu thẳng là `ClientPrivileges` (cờ wire của osu! client) trên `UserData.Privilege`; silence là
`UserData.SilenceEndsAt`. Input SSE `/users/{id}/live` chỉ có vì bot spectate mọi người lúc đăng nhập.

Định vị mới: **"A lightweight, high-performance osu!(stable) backend platform specialized for multiplayer."** Server
cung cấp capability lõi; bot và tính năng khác là client tích hợp bên trên. Mọi client (osu!, osu!tourney, IRC, REST)
authenticate thành một User, nhận một phiên (`Connection`) có token, và hành động bằng identity + permissions + domain
rules. Server chạy bình thường khi không có BasilBot.

## Quyết định đã chốt (người dùng, 2026-10-05)

1. **Server không có khái niệm Bot.** Không nhãn Bot, không loại phiên Bot, không người dùng ảo trong game.
   * BasilBot là một user dùng API với những permission được cấp.
   * Người dùng nhắn tin qua API hiển thị trong game như đang offline.
   * `!mp make` và mọi lệnh chat là trách nhiệm của bot. Bot tra permission của người gửi qua API thay vì giữ bảng
     quyền riêng; server vẫn kiểm lại.
   * API phải đủ rộng để bot làm được mọi việc của nó.
2. **Server chỉ phát event.** Countdown, "Good luck", cảnh báo anticheat, DM referee là việc của bot. Không có bot
   thì chat im lặng, chức năng vẫn chạy. IRC TOPIC khi đổi tên phòng lấy nguồn là server.
3. **Permission chi tiết theo hành động, kiểu Discord.**
   * Chỉ quyền quản lý cấp cao mới được rộng.
   * Nhóm theo danh mục `ClientPrivileges` (`Player.*`, `Moderator.*`, …): một enum, tiền tố tên + mask danh mục.
   * Bit `ClientPrivileges` của một danh mục bật khi người dùng có ít nhất một quyền **hiệu lực** trong danh mục đó.
4. **Restriction là bản ghi riêng có thời hạn,** kiểu `MenuBanner`, lưu trong repository riêng (không trên
   `IUserRepository`).
   * Mỗi bản ghi mang một mask permission: bit bật là quyền đó bị tạm dừng tới khi hết hạn.
   * Silence là một restriction.
   * Permission cấp cho user vẫn nằm trên `User` (`IUserRepository`).
   * Quyền hiệu lực = quyền được cấp trừ mask của các restriction đang hiệu lực.
5. **Thứ bậc kiểu Discord.** Staff không tác động được lên người ngang hoặc cao hơn mình (gộp điểm 4, 5 cũ, mục 1.6).
6. **Input SSE:** phiên Api đã đăng nhập subscribe input thì là spectator thật; ẩn danh chỉ nhận status.
7. **Ủy quyền bằng permission.** Người có `TournamentActForUsers` hành động thay một user đang online, chỉ trong thao
   tác phòng và sảnh; server xét quyền và ghi nhận cho user đó.
8. **Hành vi đã duyệt:**
   * IRC giữ idle 300 s như Bancho; chỉ Api 2 giờ;
   * restriction tạm dừng quyền;
   * phòng tạo từ mọi transport đều xếp game client của creator nếu online và rảnh;
   * chính chủ tự thu hồi phiên của mình;
   * nhiều phiên Api mỗi user;
   * phòng luôn có creator.
9. **Phạm vi code: Domain / Protocol / Application.** Các layer khác và test chỉ ghi chú. Ngoại lệ đã được yêu cầu:
   AGENTS.md, README, `working-scopes.md`, chuỗi tagline ở `StartupBanner.cs`, `Basil.Host.csproj`,
   `docs-site/index.html`.

## 1. Mô hình phân quyền

Nguồn nghiệp vụ (osu-wiki qua context7, forum Tournament Committee):
* staff giải: host/organiser, mappool selector, mapper, playtester, referee, streamer, commentator, statistician;
* restricted không multiplayer, không chat, không PM, không làm staff;
* silence chặn chat, PM và vào phòng multiplayer có thời hạn;
* `addref` chỉ creator, referee quản lý phòng nhưng không quản lý referee;
* osu!tourney cần supporter;
* `makeprivate` ẩn lịch sử trận;
* mappool và điểm qualifier là thông tin đặc quyền;
* tự động hóa referee được phép nhưng người thật phải giành lại được phòng.

### 1.1 Actor, resource, cơ chế

* **Actor:** tài khoản đã đăng nhập, hành động qua một `Connection` (osu!, osu!tourney, IRC, Api), hoặc qua phiên của
  người được ủy quyền. Ẩn danh chỉ đọc dữ liệu công khai (downstream).
* **Resource:**
  * tài khoản (hồ sơ, mật khẩu, permission, restriction, phiên);
  * phòng / trận;
  * kênh (chung, phòng, spectator, PM);
  * beatmapset;
  * nội dung server;
  * cấu hình và chẩn đoán.
* **Ba cơ chế, không thêm cơ chế khác:**
  1. **Permission được cấp** trên tài khoản (`UserData.Permissions`), trả lời "tài khoản này được làm gì".
  2. **Restriction** (bản ghi có thời hạn): tạm dừng một tập permission. Silence và restrict đều là restriction.
  3. **Vai trò trong phòng**, suy từ dữ liệu phòng:
     * creator (sở hữu, vĩnh viễn);
     * referee (≤ 8, do creator thêm);
     * host (connection osu! giữ host);
     * player (ngồi slot);
     * observer (osu!tourney).

  Thao tác phòng được phép khi actor có vai trò phòng đủ mạnh **hoặc** `TournamentManageAnyRoom`. Mọi kiểm đều dùng
  quyền **hiệu lực**.
* **Thứ bậc:** khi một hành động staff nhắm vào người khác (silence, restrict, gỡ restriction, đổi permission, đặt
  mật khẩu, thu hồi phiên, xóa), actor phải giữ **mọi** permission được cấp của mục tiêu **và** ít nhất một permission
  mà mục tiêu không có (tập quyền chặt lớn hơn).
  * So trên quyền được cấp, không phải quyền hiệu lực.
  * Hai moderator ngang nhau không tác động được lên nhau.
  * Admin hẹp không đụng được owner.
  * Tự tác động lên chính mình không qua luật này.

### 1.2 Danh mục permission

`[Flags] public enum Permissions : ulong` ở `src/Basil.Domain/Users/Permissions.cs`:
* Mỗi danh mục chiếm một byte.
* Tên có tiền tố danh mục.
* Mỗi danh mục có một thành viên mask cùng tên (`Permissions.Player` = mọi bit `Player*`) để suy ra `ClientPrivileges`.
* Docs hiển thị dạng `Player.CreateRoom`.

| Quyền | Cho phép | Mặc định người mới |
|---|---|---|
| **Player** → `ClientPrivileges.Player` | | |
| `PlayerCreateRoom` | Tạo phòng (phòng thường in game và phòng giải, qua mọi transport). Phòng giải tối đa 4 phòng đang mở mỗi creator | ✓ |
| `PlayerJoinRoom` | Vào phòng và ngồi slot, kể cả khi được referee xếp chỗ | ✓ |
| `PlayerChat` | Gửi tin vào kênh chung, kênh phòng, kênh spectator mà mình là thành viên | ✓ |
| `PlayerPrivateMessage` | Gửi PM | ✓ |
| `PlayerSpectate` | Spectate người chơi khác | ✓ |
| **Supporter** → `ClientPrivileges.Supporter` | | |
| `SupporterDirect` | osu!direct trong client: tìm và tải beatmapset (host kiểm) | ✓ |
| **Tournament** → `ClientPrivileges.Tournament` | | |
| `TournamentObserveRooms` | Đăng nhập osu!tourney, quan sát mọi phòng đang mở (osu!tourney hoặc API) và đọc kênh của phòng. Cũng bật `Supporter` vì osu!tourney đòi supporter | |
| `TournamentPostInAnyRoom` | Gửi tin vào kênh của mọi phòng | |
| `TournamentViewPrivateMatches` | Thấy phòng và lịch sử trận private của người khác | |
| `TournamentManageAnyRoom` | Quản lý cấp cao: quyền creator ở mọi phòng (kể cả `addref`/`removeref`), đọc và ghi kênh mọi phòng, vào phòng không cần mật khẩu | |
| `TournamentUnlimitedRooms` | Mở phòng giải vượt giới hạn 4 | |
| `TournamentActForUsers` | Hành động thay user đang online, chỉ thao tác phòng và sảnh | |
| `TournamentManageBeatmaps` | Nhập, thay, xóa, ẩn, khóa beatmapset; thấy beatmapset ẩn | |
| `TournamentViewHiddenBeatmaps` | Thấy beatmapset ẩn (mappool chưa công bố) | |
| **Moderator** → `ClientPrivileges.Moderator` | | |
| `ModeratorSilence` | Silence: restriction tập `Silence` có thời hạn | |
| `ModeratorRestrict` | Restriction tùy mask, có hoặc không có hạn; gỡ restriction | |
| `ModeratorMessageAnyone` | PM vượt chặn và chế độ chỉ-bạn-bè | |
| `ModeratorAnnounce` | Thông báo popup tới người online | |
| **Developer** → `ClientPrivileges.Developer` | | |
| `DeveloperConfigureServer` | Mirror, khóa đăng ký, cấu hình lúc chạy | |
| `DeveloperViewDiagnostics` | Xem chẩn đoán | |
| **Owner** → `ClientPrivileges.Owner` | | |
| `OwnerManageAccounts` | Tạo, sửa, xóa tài khoản; đặt mật khẩu người khác; thu hồi phiên người khác | |
| `OwnerManagePermissions` | Cấp và gỡ permission (chỉ những bit mình có) | |
| `OwnerManageContent` | MOTD, menu, FAQ, seasonal, banner | |

Ghi chú:
* **Tập silence** là hằng ở Domain: `PlayerJoinRoom | PlayerChat | PlayerPrivateMessage`, theo osu-wiki: silence
  chặn chat, PM và vào multiplayer. `PlayerCreateRoom` không cần nằm trong tập, vì tạo phòng in game đòi cả
  `PlayerJoinRoom`.
* Hàm Domain:
  * `Allows(required)`: đủ mọi bit;
  * `ToClientPrivileges()` áp lên quyền hiệu lực;
  * `Effective(restrictions, now)`: quyền được cấp trừ mask restriction đang hiệu lực.
* `ClientPrivileges` ở lại `Domain/Client`, chỉ là giá trị suy ra. Xóa `ClientPrivilegesExtensions.Has` vì không
  còn ai dùng.

Preset vai trò (chỉ là bảng trong docs; seed ở downstream dùng nó):

| Vai trò | Permissions |
|---|---|
| Player (mặc định) | `Player.*`, `SupporterDirect` |
| Streamer / Commentator | Player + `TournamentObserveRooms` |
| Statistician | Player + `TournamentObserveRooms`, `TournamentViewPrivateMatches` |
| Referee toàn giải | Player + `TournamentManageAnyRoom`, `TournamentObserveRooms`, `TournamentViewPrivateMatches` |
| Mappooler | Player + `TournamentManageBeatmaps` |
| Playtester / Mapper | Player + `TournamentViewHiddenBeatmaps` |
| Moderator | Player + `Moderator.*` |
| Host / Organiser | tất cả |
| BasilBot | `PlayerChat`, `PlayerPrivateMessage`, `TournamentObserveRooms`, `TournamentPostInAnyRoom`, `TournamentViewPrivateMatches`, `TournamentActForUsers`, `ModeratorMessageAnyone` |

### 1.3 Ma trận hành động phòng

✓ = vai trò đủ. Mọi dòng có ✓ ở cột manager cũng cho phép `TournamentManageAnyRoom`. Manager = creator hoặc
referee, và phải còn quyền hiệu lực (bị restrict toàn bộ thì không quản lý được).

| Hành động | Creator | Referee | Host | Player | Thêm điều kiện |
|---|---|---|---|---|---|
| Tạo phòng thường (in game) | | | | | `PlayerCreateRoom` + `PlayerJoinRoom`, connection osu!, chưa ở phòng nào |
| Tạo phòng giải | | | | | `PlayerCreateRoom`; tối đa 4 trừ `TournamentUnlimitedRooms`. Creator được xếp chỗ nếu có `PlayerJoinRoom` |
| Vào phòng | | | | | `PlayerJoinRoom`, không bị cấm, đúng mật khẩu (`TournamentManageAnyRoom` bỏ qua) |
| Đổi cài đặt | ✓ | ✓ | ✓ | | đổi private chỉ manager |
| Khóa phòng, di chuyển, xếp slot, đổi team người khác | ✓ | ✓ | | | |
| Khóa một slot, bắt đầu round, đặt host, kick | ✓ | ✓ | ✓ | | không kick manager |
| Abort, countdown, ban/unban, đóng phòng | ✓ | ✓ | | | không ban manager |
| Xếp chỗ người khác | ✓ | ✓ | | | người được xếp cần `PlayerJoinRoom`; actor là manager cả phòng cũ |
| Mời | ✓ | ✓ | | ✓ (đang ngồi) | |
| Thêm / bỏ referee | ✓ | | | | |
| Đọc kênh phòng | ✓ | ✓ | ✓ | ✓ | observer cũng đọc; `TournamentObserveRooms` đọc mọi phòng |
| Gửi vào kênh phòng | ✓ | ✓ | ✓ | ✓ | cần `PlayerChat`. Observer osu!tourney đang ở trong phòng cũng gửi được (như hiện nay). Người chỉ đọc nhờ `TournamentObserveRooms` thì không gửi được; `TournamentPostInAnyRoom` gửi mọi phòng |
| Thao tác slot của chính mình | | | | ✓ | connection osu! của chính người đó |

### 1.4 Ma trận hành động tài khoản, chat, phiên

| Hành động | Quyền | Điều kiện |
|---|---|---|
| Đăng nhập osu! / IRC / Api | (không cần) | tài khoản chưa xóa, đúng mật khẩu; luật số phiên |
| Đăng nhập osu!tourney | `TournamentObserveRooms` | — |
| Gửi kênh chung | `PlayerChat` + `GeneralChannel.WritePermissions` | là thành viên |
| Vào kênh chung | `GeneralChannel.ReadPermissions` | — |
| PM | `PlayerPrivateMessage` | không bị chặn, friends-only (`ModeratorMessageAnyone` vượt). Người nhận đang bị tạm dừng `PlayerPrivateMessage` thì trả `TargetSilenced` |
| Spectate | `PlayerSpectate` | connection osu!, osu!tourney hoặc Api |
| Silence | `ModeratorSilence` | thứ bậc; có thời hạn |
| Restrict / gỡ restriction | `ModeratorRestrict` (gỡ silence: `ModeratorSilence` cũng được) | thứ bậc |
| Đổi mật khẩu | chính chủ (kèm mật khẩu cũ) hoặc `OwnerManageAccounts` + thứ bậc | đóng mọi phiên |
| Tạo tài khoản | `OwnerManageAccounts` | bit ban đầu ⊆ bit của actor |
| Xóa tài khoản | `OwnerManageAccounts` | thứ bậc |
| Đổi permission | `OwnerManagePermissions` | thứ bậc; chỉ thêm/bỏ bit actor có |
| Thu hồi phiên | chính chủ, hoặc `OwnerManageAccounts` + thứ bậc | — |
| Hành động thay user | `TournamentActForUsers` | user online; quyền xét theo user đó; chỉ phòng và sảnh |
| Thông báo popup | `ModeratorAnnounce` | — |
| Beatmap, nội dung, cấu hình, chẩn đoán | bit tương ứng | không có actor ở Application; host gác bằng policy |

Kết quả phân biệt hai lý do. Quyền bị một restriction tạm dừng thì trả `Silenced` (hoặc `TargetSilenced`). Quyền
không được cấp thì trả `NotAuthorized` / `NoPermission`. Client nhờ đó báo đúng lý do.

### 1.5 Tầng thực thi

* **Domain:**
  * `Permissions`, `Allows`, `Effective`, `ToClientPrivileges`;
  * `Restriction` và `IsActive(now)`;
  * tính hợp lệ (`User.Id ≥ 1`, restriction có mask khác `None`, `EndsAt > StartsAt`).
* **Application Services:** mọi thao tác có actor (`by`).
  * Quyền hiệu lực của người online lấy từ `UserSession.Restrictions` (nạp lúc đăng nhập, cập nhật khi
    restrict/gỡ). Người offline lấy từ repository.
  * Không phụ thuộc transport. Không chỗ nào xét authority bằng `ConnectionType` hay `is XConnection`; loại client
    chỉ quyết định làm được gì về kỹ thuật.
* **Storage:** không quyết định gì; lọc theo tiêu chí tường minh do caller đặt.
* **Host (downstream):**
  * xác thực token → `Connection`;
  * policy theo permission cho route không có actor;
  * cờ query (`IncludePrivate`, `IncludeHidden`) từ permission;
  * `ClientPrivileges` lên wire;
  * **phạm vi ủy quyền:** chỉ route phòng và sảnh (`ILobbyService.OpenAsync/CloseAsync`, các service con của
    `IRoomService`) nhận header ủy quyền.
  * Lý do ủy quyền chia hai tầng: Application quyết định *ai* làm thay *ai* (`ISessionService.ActFor`); host giới hạn
    *phạm vi*, vì connection ủy quyền không phân biệt được với connection thật trong mọi service. AGENTS.md ghi luật
    này.
* **Client:** không bao giờ là nơi thực thi. Bot tra quyền qua API để trả lời cho đúng; server vẫn kiểm lại.

### 1.6 Rủi ro và trường hợp biên

* **Leo thang:** thứ bậc + "chỉ trao bit mình có".
  * Người giữ `OwnerManagePermissions` cuối cùng tự gỡ quyền thì bị khóa ngoài. Downstream cần lối khôi phục từ
    host (CLI).
  * Hai owner đầy đủ quyền không tác động được lên nhau (ngang hàng). Đó là chủ đích.
* **Ủy quyền:** lộ token của tài khoản có `TournamentActForUsers` là làm thay được user online, nhưng chỉ trong phòng
  và sảnh (ghi `ponytail:`).
  * Quyền là của user đó.
  * Event ghi nhận user đó; ai đã ủy quyền chỉ có trong log host.
* **Restriction hết hạn tự nhiên,** không cần timer: quyền hiệu lực tính theo `now` mỗi lần kiểm. `ClientPrivileges`
  trên client osu! chỉ cập nhật khi host gửi lại: downstream gửi khi có `UserRestricted`/`UserRestrictionLifted`/
  `UserPermissionsChanged`. Lúc hết hạn: client tự biết silence end; restriction khác cập nhật ở lần đăng nhập sau
  hoặc qua một vòng nền ở host.
* **Hiệu lực ngay:** đổi permission hay restriction áp ngay cho phiên online. Mất quyền dùng osu!tourney (kiểm bằng
  quyền hiệu lực) thì đóng phiên osu!tourney.
* **Phiên Api không hiện online trong game,** nhưng vẫn vào kênh PM nên vẫn nhận PM.
* **Luật giải đấu ngoài server** (staff không được chơi, staff list, tournament ban) là chính sách của từng giải.
  Basil không mô hình hóa giải đấu hay đội.
* **Xóa tài khoản:** permission về `None`, đóng mọi phiên, đăng nhập bị từ chối.

## 2. Thay đổi theo project

### Domain
* `Users/Permissions.cs` mới: enum + mask danh mục, hằng `Silence`, `Allows`, `Effective`, `ToClientPrivileges`.
* `Users/Restriction.cs` mới, khuôn `IWrapper` như `User`:
  * `Restriction { Id; Value }`;
  * `RestrictionData { User User; Permissions Permissions; DateTimeOffset StartsAt; DateTimeOffset? EndsAt }`
    (`EndsAt` null là tới khi gỡ);
  * setter kiểm mask khác `None` và `EndsAt > StartsAt`;
  * `IsActive(now)`;
  * gỡ sớm = đặt `EndsAt = now`, giữ lịch sử.
* `UserData`:
  * `Privilege` → `Permissions` (mặc định preset Player);
  * bỏ `SilenceEndsAt`.
* `User.Id` init ném `ArgumentOutOfRangeException` khi `≤ 0`.
* Xóa `Users/SystemUserIds.cs`.
* `GeneralChannel.ReadPrivilege/WritePrivilege` → `ReadPermissions/WritePermissions: Permissions`, mặc định `None`
  (gửi tin luôn cần `PlayerChat`).
* `EnumExtensions.ThrowIfUndefined` đã nhận tổ hợp cờ.

### Protocol
* `RoomPacket`: hằng `NoHostId = 0`, giá trị wire khi phòng không có host. Thay `?? SystemUserIds.BasilBot` ở
  Host.Bancho.

### Storage
* `Users/IRestrictionRepository.cs` mới, khuôn như `IMenuBannerRepository` và động từ AGENTS.md:
  * `CreateAsync(RestrictionData) → Restriction`;
  * `CreateOrUpdateAsync(Restriction)`;
  * `GetAsync(int id)`;
  * `ListAsync(User) → IReadOnlyList<Restriction>` (toàn bộ lịch sử của user; service lọc cái đang hiệu lực).
* `Connection`:
  * thêm `Token`;
  * xóa `BotConnection`, thêm `ApiConnection`;
  * `ConnectionType` = `Bancho, Tourney, Irc, Api`.
* `UserSession`:
  * bỏ accessor `Bot`, thêm `Apis`;
  * thêm `Restrictions` (danh sách restriction chưa hết hạn, `internal set`).
* `UserRegistry.Find(string token)`: chỉ mục token, cập nhật qua `internal Index/Unindex`.
* `UserQuery`: `Permissions? Permissions` thay `Privilege`; cú pháp `permission=<mask|tên>`.
* Remarks `IUserRepository.CreateAsync`: id luôn ≥ 1.

### Contracts (tôi tự viết chữ ký và XML doc)
* `ISessionService`:
  * xóa `OpenBotAsync`;
  * `Announce(Connection by, string text, IReadOnlyCollection<User>? to) → int?`: cần `ModeratorAnnounce`; `null` khi
    không đủ quyền;
  * `Revoke(Connection by, User user) → bool`: chính chủ hoặc `OwnerManageAccounts` + thứ bậc; lý do `Revoked`;
  * `ActFor(Connection by, User user) → (Connection? Connection, DelegationFailure? Failure)`:
    * `DelegationFailure` = `NotPermitted`, `UserOffline`;
    * thứ tự chọn phiên: Bancho > IRC > Api > Tourney;
    * XML doc nêu phạm vi (chỉ thao tác phòng và sảnh);
    * comment `ponytail:` về trần rủi ro.
* `IAuthService`:
  * `LoginAsync` nhận `Api`, client `null` cho IRC/Api; nạp restriction đang hiệu lực vào session;
  * `CreateAccountAsync(Connection by, UserData, Md5)`;
  * `ChangePasswordAsync(Connection by, User user, Md5 newHash, Md5? currentHash)`: đóng mọi phiên, lý do
    `CredentialsChanged`;
  * kết quả: enum `AccountResult` (`Ok`, `NotAuthorized`, `WrongPassword`, `NameTaken`, `InvalidName`).
* `IUserService` (mọi thao tác nhận `Connection by`, trả `AccountResult` hoặc `bool`, không còn ngoại lệ BasilBot):
  * `SilenceAsync(by, user, endsAt)`: tạo restriction tập `Silence`;
  * `RestrictAsync(by, user, Permissions, DateTimeOffset? endsAt) → Restriction?`;
  * `LiftAsync(by, Restriction)`: đặt `EndsAt = now`;
  * `SetPermissionsAsync(by, user, permissions)`;
  * `DeleteAsync(by, user)`.
  * Đổi permission hoặc restriction mà làm mất quyền osu!tourney hiệu lực thì đóng phiên osu!tourney (lý do
    `Revoked`).
* `UserEvents`:
  * `UserSilenced` → `UserRestricted(User, Restriction)`;
  * thêm `UserRestrictionLifted(User, Restriction)`, `UserPermissionsChanged(User, Permissions)`;
  * `ConnectionCloseReason` thêm `CredentialsChanged`, `Revoked`.
* `LoginResult.NoTourneyPermission` giữ (thiếu `TournamentObserveRooms` hiệu lực).
* `ILobbyService.OpenAsync(Connection by, string name, string password, bool isTournament, bool isPrivate,
  MatchSettings? settings)`: creator = `by.User`; phòng thường cần `by` là `BanchoConnection`.
* XML doc của `IRoomService.*`, `IChannelService`, `IChannelSpectatorService`: bỏ "BasilBot", nói theo permission.

### Services
* Helper nội bộ `Users/PermissionRules`:
  * quyền hiệu lực của connection (từ session) hoặc của user offline (repository);
  * `Outranks(actor, target)` (thứ bậc);
  * `Check(...) → Allowed | NotGranted | Suspended`.
* `SessionService`:
  * xóa mọi thứ của bot;
  * `Open` sinh token 32 byte base64url (`RandomNumberGenerator`) và đánh chỉ mục; Tourney cần
    `TournamentObserveRooms` hiệu lực;
  * `ConnectionRules`: `AllowsMany` (Tourney, Api), `IdleTimeout` (300 s, Api 2 giờ);
  * Api vào kênh PM;
  * thêm `Announce(by)`, `Revoke`, `ActFor`.
* `AuthService`: bỏ từ chối user 0 và loại Bot; tạo `ApiConnection`; nạp restriction; thêm `CreateAccountAsync(by)` và
  `ChangePasswordAsync`.
* `UserService`: silence, restrict, gỡ, đổi permission, xóa theo mục 1.4. Cập nhật `UserSession.Restrictions` và bản
  `User` online (`Apply`). Phát event.
* `RoomRules`:
  * `CanManage` = (manager và còn quyền hiệu lực) hoặc `TournamentManageAnyRoom`;
  * `IsCreatorOrBot` → `IsCreatorOrAnyRoomManager`.
* `RoomMembershipService`:
  * `PlayerJoinRoom` thay `Player` và thay kiểm silence (restriction tạm dừng bit thì trả `Silenced`);
  * bypass mật khẩu bằng `TournamentManageAnyRoom`;
  * xóa kiểm user 0;
  * `Invite` bỏ `target.Bot`.
* `RoomAuthorityService`: xóa kiểm user 0.
* `LobbyService`:
  * `OpenAsync(by, …)` kiểm `PlayerCreateRoom` (+ `PlayerJoinRoom` cho phòng thường) và giới hạn 4 trừ
    `TournamentUnlimitedRooms`;
  * bỏ đoạn cho bot vào kênh phòng.
* `ChannelService`:
  * kênh chung theo `Read/WritePermissions`;
  * kênh phòng:
    * đọc = player, observer trong phòng, manager, `TournamentManageAnyRoom`, `TournamentObserveRooms`;
    * gửi = player, observer trong phòng, manager, `TournamentManageAnyRoom`, `TournamentPostInAnyRoom`;
  * gửi tin cần `PlayerChat`; PM cần `PlayerPrivateMessage`;
  * vượt chặn PM bằng `ModeratorMessageAnyone`;
  * `TargetSilenced` theo restriction của người nhận.
* `ChannelSpectatorService`: Bancho, Tourney, Api; cần `PlayerSpectate` (observer osu!tourney: `TournamentObserveRooms`).
* `DependencyInjection`: không thêm service mới. `IRestrictionRepository` do host đăng ký như các repository khác.

### Tài liệu định hướng (làm ngay)
* **AGENTS.md** (tôi viết):
  * dòng mô tả dự án;
  * thay "BasilBot is an ordinary user with fixed rules" và "The API acts as BasilBot" bằng các luật:
    * identity + permissions;
    * không có bot trong server;
    * permission chi tiết theo danh mục `ClientPrivileges`, `ClientPrivileges` chỉ suy ra;
    * restriction là bản ghi có thời hạn;
    * thứ bậc;
    * ủy quyền (Application quyết ai, host giới hạn phạm vi);
    * phiên và token;
    * không xét authority theo loại connection;
  * danh sách `ConnectionType`, mục Authority, mục Scope, checklist cuối;
  * thêm `Users/` có `IRestrictionRepository` vào sơ đồ feature folder.
* **README**: tagline và các gạch đầu dòng tính năng (BasilBot là client riêng, API theo user).
* **`working-scopes.md`**:
  * định vị;
  * in-scope: BasilBot là client ngoài; HTTP API theo user, đủ rộng cho client tích hợp; bearer session; permission;
  * sửa các dòng out-of-scope "general-purpose public API", "ApiKey removed", "BasilBot" (vẫn không tương thích
    osu-web v1/v2, không OAuth).
* **Tagline** ở `StartupBanner.cs`, `Basil.Host.csproj`, `docs-site/index.html`.

## 3. Ghi chú cho layer sau (không làm đợt này)

* **Infrastructure**
  * Migration SQLite:
    * user id 0 → tài khoản BasilBot thường có id mới (cập nhật FK), hoặc xóa;
    * cột `Privilege` → `Permissions`. Giá trị `UserPrivileges` cũ của `main`:
      * Unrestricted → `Player.*`, `SupporterDirect`;
      * Donator → `TournamentObserveRooms`;
      * Moderator → `Moderator.*`;
      * TourneyManager → `TournamentManageAnyRoom`;
      * Administrator/Developer → tất cả;
      * thiếu Unrestricted → một restriction toàn bộ, không hạn;
    * `SilenceEnd` → restriction tập `Silence`;
    * bảng `Restrictions` mới (`SqliteRestrictionRepository`);
    * GeneralChannel read/write → Permissions;
    * seed tài khoản quản trị đầu tiên và BasilBot có mật khẩu (cách cấp mật khẩu hỏi khi làm).
  * Bỏ handler bot spectate lúc đăng nhập, `GuidTokenGenerator`, token `bancho-bot-session`.
  * Sửa `SqliteLoginRepository` lệch schema.
  * Dispatcher: bỏ mọi lời nói của bot (countdown, anticheat, "Beatmap not found").
* **Host.Api** (phải đủ rộng cho bot và client tích hợp):
  * Xác thực: bearer token → `UserRegistry.Find` → `ApiConnection`; `MarkActive` mỗi request và định kỳ khi SSE mở.
    `/auth/login` (mật khẩu rõ qua HTTPS, host băm MD5), `/auth/logout`, `/me`.
  * User: đọc permission được cấp, quyền hiệu lực và restriction, để bot tra quyền người gửi. Quản lý
    permission/restriction/tài khoản/phiên.
  * Phòng: mọi thao tác của `ILobbyService`/`IRoomService` (tạo, cài đặt, slot, round, countdown, referee,
    host, ban, kick, mời, xếp chỗ, đóng). Header ủy quyền → `ActFor`, chỉ ở các route này.
  * Chat: liệt kê / vào / rời kênh, gửi tin kênh và PM, away, PM-private.
  * Spectate: spectate qua `ApiConnection`; `/users/{id}/live` có input khi subscriber đã đăng nhập.
  * SSE theo phiên: tin kênh/PM, event phòng/lobby, anticheat (theo permission). EventSource không gửi header, cần
    cách truyền token cho `/live`.
  * Policy permission cho beatmap, nội dung, cấu hình, chẩn đoán.
  * Khóa admin chỉ còn cho đăng ký; bỏ khỏi xác thực.
  * Bỏ mọi chặn id 0 và ảnh `basilbot.png`.
* **Host.Bancho / Host.Irc**
  * `ClientPrivileges` = quyền hiệu lực → `ToClientPrivileges()`. Gửi lại khi có event permission/restriction.
  * `AccountRestricted` khi danh mục Player hiệu lực rỗng.
  * SilenceEnd lấy từ restriction đang tạm dừng `PlayerChat`.
  * Presence chỉ tính phiên trong game (Bancho, IRC).
  * Hằng `RoomPacket.NoHostId`; TOPIC nguồn server; cho-token = `Connection.Token`.
  * osu!direct kiểm `SupporterDirect`.
  * Tiền tố IRC `@` = manager phòng hoặc `TournamentManageAnyRoom`.
* **BasilBot**: project riêng, client HTTP + SSE.
  * Tái dùng Domain và DTO API (DTO ở project chung với Host.Api).
  * Chạy ngoài hoặc trong cùng process host qua cùng client, đăng nhập bằng tài khoản thường.
  * Tra quyền người gửi qua API trước khi chạy lệnh.
  * Lệnh lấy từ git `c5e09732` (`Chat/ChatCommands.cs`, `Multiplayer/Commands/*`, `MpReplies.cs`).
* **Docs còn lại** (viết lại khi migrate layer tương ứng):
  * `privileges.md` → `permissions.md` (sửa chủ sở hữu chủ đề trong `docs/index.md`);
  * `chat.md`, `irc.md`, `multiplayer.md`, `database.md`, `basil-bot.md`;
  * `configuration.md` (khóa admin, `Basil:Bot`);
  * `for-client/api/*`, ADR-007/008, docs-site.

## 4. Pha thi công và giao việc

OpenCode server có sẵn ở `http://127.0.0.1:4096`. Lệnh mẫu:
`opencode run --server http://127.0.0.1:4096 -m <model#variant> --auto --title <t> "<prompt>"`.
* Lần gọi đầu thử variant `#high`; nếu model không nhận thì bỏ variant.
* Prompt nào cũng kèm:
  * file cần sửa;
  * chữ ký và XML doc đầy đủ (không để agent tự chọn);
  * luật AGENTS.md liên quan;
  * yêu cầu dùng Rider MCP (rename/safe-delete) và `codegraph explore`;
  * lệnh build kiểm.
* Lưu ý bắt buộc trong prompt:
  * Pha 4a: đóng phiên osu!tourney theo quyền hiệu lực; thứ bậc áp cho mọi hành động staff nhắm vào người khác.
  * Pha 4b: manager cần quyền hiệu lực; không còn đọc `SilenceEndsAt`.
* Tôi review diff từng dòng theo checklist AGENTS.md.
* Hết quota hoặc agent làm kém: chuyển sang subagent Claude (Sonnet cho việc thiết kế, Haiku cho việc cơ học), rồi tôi
  tự làm.

| Pha | Việc | Người làm / model | Kiểm |
|---|---|---|---|
| 0 | Lưu plan vào `plans/`, cập nhật memory, viết AGENTS.md | tôi | đọc lại |
| 1 | Domain (`Permissions`, `Restriction`, `UserData`, `User.Id`, `GeneralChannel`, xóa `SystemUserIds`) + Protocol `NoHostId`; code đưa sẵn trong prompt | `opencode-go/deepseek-v4.1-flash` | build Domain, Protocol.Bancho |
| 2 | Storage (`IRestrictionRepository`, Token, `ApiConnection`, `UserSession.Restrictions`, registry, `UserQuery`) | `opencode-go/mimo-v2.6-flash` | build Storage |
| 3 | Contracts: chữ ký + XML doc | tôi | build Contracts |
| 4a | `PermissionRules`, Sessions/Auth/Users services | `opencode-go/qwen3.7-plus`; dự phòng Sonnet | build Services |
| 4b | Chat/Multiplayer services; song song với 4a, file tách rời, dùng `PermissionRules` có sẵn sau khi 4a viết xong helper | `opencode-go/minimax-m3`; dự phòng Sonnet | build Services |
| 5 | Viết lại các kịch bản bot của baseline và thêm kịch bản mới (mục 5) | `opencode-go/glm-5.3-flash`; tôi review kịch bản | `ALL PASS` |
| 6 | README, tagline, `working-scopes.md` (văn bản tôi đưa sẵn) | `opencode-go/deepseek-v4.1-flash` | grep tagline cũ = 0 |
| 7 | Review toàn bộ, commit theo nhóm pha, push | tôi | checklist |

Thứ tự: 0 → 1 → 2 → 3 → (4a viết `PermissionRules` trước, rồi 4a ‖ 4b) → 5 → 6 → 7.

## 5. Kiểm chứng

* `dotnet build src/Basil.Application.Services/Basil.Application.Services.csproj` và
  `dotnet build src/Basil.Protocol.Bancho/Basil.Protocol.Bancho.csproj` sạch lỗi.
* Grep trong Domain/Application bằng 0 với:
  * `SystemUserIds|BotConnection|ConnectionType.Bot|OpenBotAsync|BasilBot|SilenceEndsAt|Participate`;
  * `ClientPrivileges` ngoài `Domain/Client` và phép suy ra;
  * authority theo `is XConnection` / `.Type is` (rà tay chỗ còn lại: chỉ được là khả năng kỹ thuật).
* Baseline: chép `plans/storage-services-split-baseline.cs` ra thư mục tạm, `dotnet run check.cs` → `ALL PASS`.
  Viết lại P2d, P3h, P3i, P4a, P4b và kịch bản silence. Kịch bản mới:
  * token tìm được phiên, mất sau khi đóng; Api rảnh 2 giờ thì đóng, Bancho 300 s;
  * đổi mật khẩu đóng mọi phiên; tự thu hồi phiên;
  * silence: không gửi tin, không vào phòng, trả `Silenced`; hết hạn thì tự hết; PM tới người bị silence →
    `TargetSilenced`;
  * restrict toàn bộ: manager không quản lý được phòng; mất quyền osu!tourney → đóng phiên osu!tourney; gỡ restriction
    thì quyền trở lại;
  * thứ bậc: moderator không silence moderator ngang hàng; admin hẹp không đổi được permission của owner; không trao
    bit mình không có;
  * `ActFor`: không quyền, user offline, thành công (creator = user được ủy quyền);
  * tạo phòng: thiếu `PlayerCreateRoom` bị từ chối; giới hạn 4 phòng giải; `TournamentUnlimitedRooms` được miễn;
  * `ModeratorMessageAnyone` vượt friends-only; thiếu `PlayerPrivateMessage` không PM được; thiếu `PlayerSpectate`
    không spectate được;
  * `TournamentObserveRooms` đọc được kênh phòng nhưng không gửi; `TournamentPostInAnyRoom` gửi được;
  * Api spectate được;
  * `ToClientPrivileges` theo quyền hiệu lực (silence không tắt `Player` vì còn `PlayerCreateRoom`/`PlayerSpectate`;
    restrict toàn bộ → `None`);
  * user id 0 bị Domain từ chối.
* Rà diff theo mục "Final verification" của AGENTS.md.

## 6. Ghi chú triển khai (Domain / Protocol / Application, 2026-10-05)

Đã làm ở `a59d43e3` (định vị, AGENTS.md, working-scopes), `1607988a` (code) và các commit sửa sau đó.

* **Luật mới, cần người dùng duyệt:** không ai tự silence, restrict hay gỡ restriction của chính mình. Lý do: nếu
  không, moderator bị silence tự gỡ được silence của mình. Luật này nằm trong `UserService.MayRestrict` và AGENTS.md.
* **Ủy quyền không xét thứ bậc** (có review bảo mật nêu ra). Đây là chủ đích: bot phải làm thay được cả referee có
  quyền cao hơn nó. Phạm vi do host giới hạn (chỉ phòng và sảnh); trần rủi ro ghi bằng `ponytail:` ở
  `SessionService.ActFor`.
* **Quyền được cấp đọc từ `connection.Session.User`** (`PermissionRules.Granted`), không từ `Login.User`. Mỗi lần đăng
  nhập đọc user từ repository (có cache), nên các phiên của cùng một user có thể giữ những bản `User` khác nhau;
  `UserService.Apply` chỉ giữ bản của session luôn mới. Kịch bản N12 của baseline kiểm điều này.
* **Phối hợp nội bộ:** `AuthService` và `UserService` gọi trực tiếp `SessionService` cụ thể (`Open`, `NewToken`,
  `CloseAll`, `CloseDisallowed`). Đây là quan hệ sở hữu: mở phiên khi đăng nhập; đóng phiên khi đổi mật khẩu, xóa
  user hoặc mất quyền là luật bảo mật trong cùng thao tác.
* **Cập nhật `UserSession.Restrictions`** chạy trong `UserRegistry.Enter()`, để restrict và gỡ song song không làm
  mất nhau.
* **Baseline:** fake `Credentials` giờ kiểm mật khẩu thật. Tài khoản `Staff` (`Permissions.All`, phiên Api) thay
  BasilBot trong mọi kịch bản. Thêm kịch bản N1–N12.
* **Downstream:** repository restriction nạp `Restriction.Value.User` phải là bản hiện hành, vì `LiftAsync` xét thứ
  bậc trên bản đó. Route API nên lấy user mục tiêu từ session online nếu có (`UserRegistry.Find(user)?.User`) trước
  khi gọi các thao tác xét thứ bậc.
