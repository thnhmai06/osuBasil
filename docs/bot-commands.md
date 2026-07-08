# Các lệnh BasilBot

Bên dưới là các lệnh chat của `BasilBot` - sử dụng trực tiếp thông qua chat (kênh chung, kênh của trận,
hoặc DM cho `BasilBot`).

Prefix mặc định là `!` (admin server có thể đổi qua `ServerBehaviorOptions.CommandPrefix`). **DM trực tiếp cho
BasilBot là ngoại lệ - prefix ở đó không bắt buộc**: gõ `help` hay `!help` trong DM đều được hiểu như nhau (mọi
DM gửi cho BasilBot vốn đã chỉ được xử lý như một lệnh, không có đường rơi xuống chat/mail thường nào khác, nên
nới lỏng prefix ở đây không có rủi ro nuốt nhầm tin nhắn thường).

Để biết vì sao một khác biệt so với osu! Bancho gốc tồn tại (chủ đích hay do phạm vi dự án), xem
[`working-scopes.md`](working-scopes.md).

**Danh sách lệnh trong `!help`/`!mp help` được BasilBot tự gom lại từ 1 nguồn duy nhất trong code** (không phải
chuỗi help viết tay cố định) - thêm lệnh mới vào nguồn đó là help tự cập nhật theo, không cần sửa 2 chỗ. `!help`
chỉ liệt kê lệnh chat chung (+ `make`/`makeprivate`/`in`/`mp help` vì 3 lệnh này chạy ngoài scope trận); `!mp help`
liệt kê riêng các subcommand `!mp` cần scope trận. Một số lệnh có thêm dòng mô tả tuỳ chọn số (vd `!mp set`,
`!mp mods`, `!mp team`) - các dòng đó xuống dòng thành tin nhắn riêng khi gửi, giống mọi reply nhiều dòng khác.

---

## Lệnh chat chung

Dùng được ở bất kỳ đâu (kênh chung, kênh trận, DM), không cần quyền gì đặc biệt.

| Lệnh  | Cú pháp                      | Mô tả                                                                                                                                                                                | So sánh với BanchoBot                                                                                                                                 |
|-------|------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------|
| Help  | `!help`                      | Trả về danh sách lệnh chat chung BasilBot hỗ trợ (tự gom từ code, không đổi theo quyền của bạn)                                                                                    | BanchoBot liệt kê nhiều lệnh hơn (`stats`, `report`, `request`...) mà BasilBot không có                                                               |
| Roll  | `!roll [max]`                | Trả số ngẫu nhiên trong khoảng `0..max` (bao gồm cả 2 đầu). `max` mặc định `100` nếu bỏ trống, tối đa `2147483647`. Giá trị âm, `0`, hoặc không parse được → dùng lại mặc định `100` | BanchoBot random `1..max` (khác cận dưới - không bao giờ ra `0`). Cùng mặc định `100`                                                                  |
| Where | `!where <username>`          | Hiện quốc gia của `username` (tra qua danh sách user server lưu, hoạt động cả khi offline)                                                                                          | BanchoBot còn hiện cả thành phố nếu người đó bật chia sẻ vị trí - BasilBot chỉ hiện quốc gia                                                          |
| FAQ   | `!faq <entry>` / `!faq list` | In nội dung file FAQ do admin server chuẩn bị sẵn (mỗi dòng file = 1 tin nhắn riêng); `list` liệt kê mọi entry có sẵn. Tên entry được phép chứa dấu cách, chỉ chặn `\`, `..`         | BanchoBot còn hỗ trợ tiền tố ngôn ngữ 2 ký tự trước tên entry (vd `ru:lines`) để lấy bản dịch - BasilBot không có, chỉ có 1 bản duy nhất cho mỗi entry |

```
!help
→ BasilBot: "!roll [max] - roll a random number from 0 to max (default 100)"
→ BasilBot: "!where <username> - show a player's country"
→ BasilBot: "!faq <entry>|list - print a FAQ entry, or list every entry"
→ BasilBot: "!mp make/makeprivate <name> - create a tournament room from anywhere, scoping you to it"
→ BasilBot: "!mp in [match_id] - target/show a match you're not physically in (needs referee rights there)"
→ BasilBot: "!mp help - list multiplayer subcommands (usable while scoped to a match)"
   (mỗi dòng ở trên là 1 tin nhắn riêng - reply gốc là 1 chuỗi nhiều dòng, mỗi dòng xuống thành 1 message)

!roll
→ BasilBot: "PlayerOne rolls 42 point(s)"

!roll 50
→ BasilBot: "PlayerOne rolls 17 point(s)"

!where PlayerOne
→ BasilBot: "PlayerOne is in Vietnam"
   (nếu username không tồn tại → "GhostUser is not registered.")

!faq list
→ BasilBot: "Available FAQ entries: rules, schedule"
   (chưa có entry nào → "No FAQ entries available.")

!faq rules
→ BasilBot gửi từng dòng file rules.txt như 1 tin nhắn riêng
   (entry không tồn tại → "No FAQ entry found for 'rules'.")
```

---

## Lệnh Multiplayer

Toàn bộ lệnh điều khiển trận đấu - tạo phòng, đổi map, quản người chơi, bắt đầu trận...

### Referee và Host

**Referee** và **host** là hai quyền tách biệt hoàn toàn - không cái nào tự động bao gồm cái kia:

|                                 | Referee                                                                                     | Host                                                                               |
|---------------------------------|---------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------|
| Là ai? Có thể làm gì?           | Người được gán quyền Referee (`!mp addref`), được phép dùng lệnh `!mp` trên trận đó         | Người có mặt trong phòng, được đổi setting trực tiếp trong client (không qua chat) |
| Buộc là người chơi trong phòng? | **Không** - referee có thể điều khiển từ xa thông qua `!mp in <match_id>`                   | **Có**                                                                             |
| Mặc định là ai?                 | Người tạo phòng - dù tạo bằng `!mp make`/`makeprivate` hay bằng nút `Create Room` trong game | Người tạo phòng theo cách bình thường (nút `Create Room`)                           |

**Người tạo trận luôn tự động vừa là host vừa được thêm làm referee** ngay lúc tạo, bất kể tạo bằng `!mp make`/
`makeprivate` hay bằng nút `Create Room` trong game.

**Mọi subcommand `!mp` trừ `help`/`make`/`makeprivate`/`in` đều yêu cầu bạn là referee của trận đang chọn** -
thiếu quyền thì BasilBot **im lặng bỏ qua, không trả lời gì** (dễ nhầm là lệnh bị lag, thực ra là do thiếu quyền).

**Trận nào bạn đang điều khiển (scope)?** Mặc định là trận gắn với kênh chat bạn đang đứng trong. Muốn điều khiển
1 trận mà không cần đứng trong phòng đó → dùng `!mp in <match_id>` (xem mục 1 bên dưới); sau khi set, scope đó
**luôn được ưu tiên** hơn kênh hiện tại cho tới khi bạn đổi scope khác.

**Vòng đời phòng khác nhau tuỳ cách tạo:**

- Phòng tạo bằng `!mp make`/`makeprivate` **không tự đóng khi hết người chơi** - chỉ đóng khi có `!mp close`,
  hoặc khi referee cuối cùng bị `!mp removeref` (mất hết referee).
- Phòng tạo bình thường trong game vẫn tự đóng khi hết người chơi như osu! Bancho gốc, bất kể còn referee hay
  không.

---

### Thực thi nhiều lệnh cùng lúc

Referee nối được nhiều subcommand `!mp` cục bộ trong cùng 1 tin nhắn (không áp dụng cho `make`/`makeprivate`/
`in`/`help`, và không áp dụng cho `!roll`/`!where`/`!faq`):

| Toán tử | Ý nghĩa                                                    |
|---------|--------------------------------------------------------------|
| `;`     | Lệnh sau luôn chạy, bất kể lệnh trước thành công hay không |
| `&&`    | Lệnh sau chỉ chạy nếu lệnh ngay trước đó thành công        |

```
!mp lock; !mp start 30
→ BasilBot: "Locked the match"
→ BasilBot: "Match starts in 30 seconds"
   (mỗi subcommand trả lời riêng 1 dòng, nối lại bằng \n trong cùng 1 tin nhắn - luôn cả 2 dòng chạy,
    kể cả nếu lock thất bại vì lý do nào đó)

!mp map 123 && !mp start
→ chỉ đếm ngược nếu đổi map thành công; nếu map lỗi, BasilBot chỉ trả lời dòng lỗi của !mp map,
   !mp start không chạy nên không có dòng nào cho nó
```

Muốn `;`/`&&` xuất hiện **theo nghĩa đen** trong tham số (không bị hiểu là dấu nối lệnh) → bọc trong `"..."`:

```
!mp name "Vòng 1; Bảng A"
```

Trong chuỗi đã bọc `"..."`, dùng `\"` để ra `"` và `\\` để ra `\`.

**Nếu bất kỳ đoạn nào trong chuỗi không phải subcommand `!mp` hợp lệ** (ví dụ lẫn `!roll`, hoặc `!mp make`)
BasilBot sẽ từ chối toàn bộ chuỗi, không đoạn nào chạy - tránh tình huống chạy nửa chuỗi khiến bạn tưởng nhầm
mọi thứ đã được áp dụng.

---

### 1. Tạo & chọn trận

| Lệnh         | Cú pháp                      | Mô tả                                                       |
|--------------|------------------------------|---------------------------------------------------------------|
| Make         | `!mp make <tên trận>`        | Tạo trận mới. Có thể gõ ở bất kỳ đâu, kể cả DM cho BasilBot |
| Make private | `!mp makeprivate <tên trận>` | Alias của `make` - hoàn toàn giống nhau                      |
| In           | `!mp in [match_id]`          | Chuyển scope sang trận khác mà không cần đứng trong đó      |

```
!mp make Bảng A: Team Alpha vs Team Beta
→ BasilBot: "Created the match #42 Bảng A: Team Alpha vs Team Beta. You are now scoped to this match, and
   added as a referee."
   (server đầy, không tạo được → "Couldn't create the match — server is full.")
```

Bạn tự động vào slot 0 làm host **và** được thêm làm referee. BasilBot trả lời kèm **ID trận** (`#<id>`) - ghi
nhớ ID này, dùng cho `!mp in` sau này hoặc để tra cứu qua API bên ngoài (xem [`api-external.md`](api-external.md)).
Không giới hạn số phòng cùng lúc tạo được. Không truyền tên → dùng mặc định `"<tên bạn>'s match"`.

```
!mp in 42
→ BasilBot: "Now targeting match #42 Bảng A: Team Alpha vs Team Beta."
   (không phải referee trận #42 → "You're not a referee of match #42.")
   (id không tồn tại → "No active match with id #42.")

!mp in
→ BasilBot: "Currently scoped to match #42 Bảng A: Team Alpha vs Team Beta."
   (chưa từng set scope → "You're not scoped to any match.")
```

---

### 2. Cấu hình phòng

| Lệnh       | Cú pháp                                              | Mô tả                                                                                      |
|------------|--------------------------------------------------------|-----------------------------------------------------------------------------------------------|
| Settings   | `!mp settings`                                       | Hiện tên trận, map, kiểu team, điều kiện thắng, mods, và danh sách slot đang có người      |
| Lock       | `!mp lock`                                           | Khoá phòng, chặn người chơi mới join                                                       |
| Unlock     | `!mp unlock`                                         | Mở khoá lại                                                                                |
| Size       | `!mp size <1-16>`                                    | Đặt số slot khả dụng                                                                       |
| Move       | `!mp move <tên> <slot 1-16>`                         | Chuyển người chơi sang slot khác                                                           |
| Host       | `!mp host <tên>`                                     | Chuyển quyền host trong phòng                                                              |
| Clear host | `!mp clearhost`                                      | Xoá host hiện tại                                                                          |
| Name       | `!mp name <tên mới>`                                 | Đổi tên trận (cắt ở 50 ký tự)                                                              |
| Password   | `!mp password [mật khẩu]`                            | Đặt mật khẩu phòng; bỏ trống để xoá mật khẩu                                               |
| Invite     | `!mp invite <tên>`                                   | Gửi lời mời vào trận cho người đang online                                                 |
| Team       | `!mp team <tên> <red\|blue>`                         | Gán team cho 1 người chơi                                                                  |
| Map        | `!mp map <beatmap id>`                               | Đổi map đang chọn, unready toàn bộ người chơi                                              |
| Mods       | `!mp mods <mods>`                                    | Đặt mod chung cho cả trận                                                                  |
| Set        | `!mp set <teammode 0-3> [scoremode 0-3] [size 1-16]` | Đặt gộp team mode + điều kiện thắng + size trong 1 lệnh                                    |

**`!mp settings`** - reply nhiều dòng, mỗi field 1 dòng riêng:

```
!mp settings
→ BasilBot: "Room name: Bảng A: Team Alpha vs Team Beta (#42)"
→ BasilBot: "Beatmap: 111222 xi - Frontier [Insane]"
→ BasilBot: "Team mode: TeamVs, Win condition: ScoreV2"
→ BasilBot: "Active mods: DoubleTime, Freemod"     (chỉ xuất hiện khi có mod/freemod đang bật)
→ BasilBot: "Players: 2"
→ BasilBot: "Slot 1  Not Ready 7 PlayerOne       [Host]"
→ BasilBot: "Slot 4  Not Ready 12 PlayerTwo       [NoFail]"
```

> **Lưu ý:** osu! Bancho gốc gắn link `osu.ppy.sh/mp/{id}` (lịch sử trận) và link hồ sơ từng người chơi vào các
> dòng này - Basil không có 2 trang đó (offline, không có lịch sử/hồ sơ public) nên chỉ hiện text/số thô: `#42`
> thay cho link lịch sử, user ID số (`7`, `12`) thay cho link hồ sơ. Muốn tra username từ ID, dùng
> `GET /users/{id}` phía admin (xem [`api-external.md`](api-external.md#42-user)) hoặc TRT snapshot
> (`GET /multi/{id}`, field `liveSlots[].userId`). Tag cuối mỗi slot (`[Host]`, `[NoFail]`, `[Host / NoFail]`)
> gộp vai trò host + mod riêng của người đó khi đang freemod.

```
!mp size 8
→ BasilBot: "Changed match to size 8"

!mp move PlayerOne 3
→ BasilBot: "Moved PlayerOne into slot 3"

!mp password
→ BasilBot: "Removed the match password"

!mp password abc123
→ BasilBot: "Changed the match password"
   (không echo lại mật khẩu vừa đặt)

!mp host PlayerTwo
→ BasilBot: "Changed match host to PlayerTwo"

!mp clearhost
→ BasilBot: "Cleared match host"

!mp name Vòng bán kết
→ BasilBot: "Room name updated to \"Vòng bán kết\""

!mp invite PlayerTwo
→ BasilBot: "Invited PlayerTwo to the room"
   (không tìm thấy user online → "User not found: PlayerTwo")
   (user đã ở sẵn trong phòng → "User is already in the room")

!mp team PlayerOne red
→ BasilBot: "Moved PlayerOne to team Red"

!mp map 111222
→ BasilBot: "Changed beatmap to xi - Frontier"
   (id không có trong DB cục bộ → "No beatmap with id 111222 found locally.")
```

> **`!mp map`** chỉ nhận `<beatmap id>` - osu! Bancho gốc còn nhận thêm tham số `<gamemode>` để đổi luôn cả chế
> độ chơi, BasilBot cố tình không có tham số đó (xem bảng so sánh cuối mục 2).

**`!mp mods`** - 3 cách dùng:

```
!mp mods HR DT HD NF
→ BasilBot: "Enabled NoFail, Hidden, HardRock, DoubleTime, disabled FreeMod"
   (danh sách mod theo thứ tự bit-value tăng dần - format mặc định của enum [Flags] trong .NET, không phải
    thứ tự bạn gõ. Danh sách chỉ gồm mod MỚI bật/tắt so với trước đó, không phải toàn bộ mod đang active -
    dùng !mp settings để xem đầy đủ mod hiện tại)

!mp mods Freemod
→ BasilBot: "Enabled FreeMod"
   (bật freemod dùng lại nguyên mod đang có: mod tốc độ (DT/HT/NC) giữ ở cấp trận, mod còn lại chuyển thành
    mod riêng của host. Gộp thêm mod mới trong cùng lệnh này, vd "!mp mods HD DT Freemod", KHÔNG được áp
    dụng - BasilBot chỉ bật freemod, bỏ qua các mod token đi kèm trong trường hợp này)

!mp mods None
→ BasilBot: "Disabled NoFail, Hidden, HardRock, DoubleTime, disabled FreeMod"
```

> Mã mod 2 ký tự hỗ trợ: `NF, EZ, HD, HR, SD, DT, RX, HT, NC, FL, SO, AP, PF` (xem `!mp help` để BasilBot tự
> liệt kê lại danh sách này). Mã không nhận diện được (vd `S2`, `FI`) bị bỏ qua lặng lẽ, không báo lỗi.

**`!mp set`** - chỉ `<teammode>` bắt buộc, `scoremode`/`size` tuỳ chọn:

```
!mp set 2 3 16
→ BasilBot: "Changed match settings to 16 slots, TeamVs, ScoreV2"

!mp set 0
→ BasilBot: "Changed match settings to HeadToHead, Score"
   (chỉ đổi team mode; win condition giữ nguyên giá trị hiện tại của trận, không phải luôn là "Score";
    không truyền size thì không có phần "N slots," ở đầu câu)
```

> Teammode: `0` HeadToHead, `1` TagCoop, `2` TeamVs, `3` TagTeamVs. Scoremode: `0` Score, `1` Accuracy,
> `2` Combo, `3` ScoreV2 - **không có tuỳ chọn `pp`**, server không tính pp làm điều kiện thắng.

> So với osu! Bancho gốc: mọi lệnh trong mục 2 hành vi khớp gốc, trừ `map` (thiếu tham số `<gamemode>`, cố
> tình không có) và `set` (thiếu giá trị `pp` cho scoremode, cố tình không có vì server không tính pp).

---

### 3. Quản lý người chơi & referee

| Lệnh           | Cú pháp               | Mô tả                                                                         |
|----------------|-----------------------|-----------------------------------------------------------------------------------|
| Add referee    | `!mp addref <tên>`    | Thêm referee cho trận - bất kỳ referee nào cũng gọi được                      |
| Remove referee | `!mp removeref <tên>` | Gỡ referee - gỡ referee cuối cùng của phòng `!mp make` sẽ **tự đóng phòng đó** |
| List referees  | `!mp listrefs`        | Liệt kê referee của trận                                                      |
| Kick           | `!mp kick <tên>`      | Đuổi người chơi ra khỏi phòng (không chặn rejoin)                             |
| Ban            | `!mp ban <tên>`       | Đuổi + chặn rejoin vĩnh viễn cho tới khi `unban`                               |
| Unban          | `!mp unban <tên>`     | Gỡ chặn rejoin đã đặt bởi `ban`                                                |
| Ban list       | `!mp banlist`         | Liệt kê người chơi đang bị chặn rejoin trận này                               |

```
!mp addref RefereeName
→ BasilBot: "Added RefereeName to the match referees"
   (không tìm thấy user online → "User not found: RefereeName")

!mp removeref RefereeName
→ BasilBot: "Removed RefereeName from the match referees"
   (gỡ referee cuối cùng của phòng !mp make → "Removed RefereeName from the match referees. No referees
    remain — match closed")

!mp listrefs
→ BasilBot: "Match referees:"
→ BasilBot: "PlayerOne"
→ BasilBot: "RefereeName"
   (chưa có referee nào → "No referees")

!mp kick TrollPlayer
→ BasilBot: "Kicked TrollPlayer from the match"

!mp ban CheaterPlayer
→ BasilBot: "Banned CheaterPlayer from the match"

!mp unban CheaterPlayer
→ BasilBot: "Unbanned CheaterPlayer from the match"

!mp banlist
→ BasilBot: "Match bans:"
→ BasilBot: "CheaterPlayer"
   (chưa ban ai → "No banned players")
```

**Kick/ban nhắm được cả referee khác** - chỉ mất quyền hiện diện vật lý trong phòng, referee bị kick/ban vẫn giữ
nguyên quyền `!mp` và điều khiển được trận từ xa qua `!mp in`. `ban` chỉ áp dụng cho người đang thực sự có mặt
trong phòng lúc bạn gõ lệnh (không tra được người offline).

---

### 4. Điều khiển trận đấu

| Lệnh        | Cú pháp            | Mô tả                                                                      |
|-------------|--------------------|--------------------------------------------------------------------------------|
| Start       | `!mp start [giây]` | Bắt đầu trận ngay, hoặc đếm ngược rồi tự start nếu có `giây`                |
| Timer       | `!mp timer [giây]` | Đếm ngược giống `start` nhưng **không** tự start khi hết giờ (mặc định 30s) |
| Abort timer | `!mp aborttimer`   | Huỷ đếm ngược đang chạy (cả của `start` lẫn `timer`)                        |
| Abort       | `!mp abort`        | Huỷ trận đang diễn ra, reset ready/loaded, đóng round hiện tại             |
| Close       | `!mp close`        | Đóng trận ngay lập tức, đuổi mọi người, ghi nhận thời điểm kết thúc        |

```
!mp start
→ BasilBot: "Match started"

!mp start 30
→ BasilBot: "Match starts in 30 seconds"
   (rồi BasilBot tự broadcast thêm vào chat trận, không phải reply cho riêng bạn:
    "Queued the match to start in 30 seconds" → "Match starts in 10 seconds" → "Match starts in 5 seconds" →
    ... → "Good luck, have fun!")

!mp timer 60
→ BasilBot: "Countdown started: 60 seconds"
   (broadcast tương tự start nhưng kết thúc bằng "Countdown finished" thay vì tự start)

!mp aborttimer
→ BasilBot: "Countdown aborted"
   (không có countdown nào đang chạy → "No countdown is running.")

!mp abort
→ BasilBot: "Aborted the match"
   (trận chưa in-progress → "Match is not in progress.")

!mp close
→ BasilBot: "Closed the match"
```

---

## Ngoài phạm vi hiện tại

Danh sách lệnh chưa triển khai và lý do (mappool, scrim engine, `!mp force`, lệnh chat cá nhân...) nằm ở
[`working-scopes.md`](working-scopes.md#ngoài-phạm-vi-hiện-tại) - không lặp lại ở đây để tránh 2 nguồn có thể lệch nhau.
