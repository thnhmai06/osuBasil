# Kế hoạch: mô hình runtime của Application (Room, Session, Event) — suy biến, tham chiếu, event theo thao tác

**Trạng thái: CHỈ LÀ KẾ HOẠCH — chưa thực thi.** Viết 2026-09-29 trên `develop` @ `f84f6d74`, sau
bốn vòng trao đổi với người dùng trong cùng phiên. Người dùng duyệt trước khi bước 1 chạy.

**Phạm vi:** `Basil.Domain` (chủ yếu dời sang Application hoặc thêm tham chiếu) và
`Basil.Application.{Models,Contracts,Services}` (phần chính). **Ngoài phạm vi:** Infrastructure,
`Basil.Host.*`, mọi test project. Các project đó hiện vẫn tham chiếu `Basil.Application.csproj`
(đã bị xoá), nên không build được và không chạy test được. Kế hoạch này không sửa chúng.

---

## 0. Nguyên tắc đã chốt

Những điều dưới đây do người dùng quyết định trong phiên này và các phiên trước. Chúng là thước
đo cho mọi thay đổi bên dưới; không mở lại nếu không có bằng chứng mới.

### 0.1 Từ các phiên trước

- **Domain** chỉ chứa mô hình nghiệp vụ tồn tại lâu dài (`Match`). **Application.Models** chứa mô
  hình chỉ tồn tại ở runtime (`Room`).
- **Event** là fact nội bộ server. **Notification** là thứ gửi cho client. Event không phải
  packet, không biết handler. Dispatcher là consumer duy nhất của một event source và fan-out tới
  0..N handler. Handler thực hiện side effect.
- Event source = `Channel<T>` của chính object. Không có class EventLog bọc ngoài. Dispatcher
  đọc bằng `await foreach` trong BackgroundService (người dùng đã chọn thiết kế này hai lần).
- Thay đổi thông tin là setter hoặc field trực tiếp, không phải method `Change*`/`Rename`/`With*`.
  Cần phát event thì phát ngay trong setter đó.
- Tài nguyên được tập trung một chỗ (cache-based), không có bản sao thứ hai. Khi thay đổi thì báo
  cho client.

### 0.2 Từ phiên này

1. **"Cái gì suy biến được thì thực hiện suy biến."** Property mà object runtime và object Domain
   *đồng sở hữu* (source of truth ở Domain) trở thành property suy biến `=>` trên object runtime.
   Setter của nó gán vào Domain rồi phát event. Kết quả: Application quản lý tương tác quanh model
   (runtime, event); Domain quản lý model và tính hợp lệ bên trong. Không expose event writer, và
   không có nơi gọi nào quên phát event.
   - Chỉ áp dụng cho property đồng sở hữu. Ví dụ `Match` là bản lưu bền của `Room`, nên `Match` là
     truth của `Name`, `IsVisible`, `Creator`. Thứ chỉ `Match` sở hữu (`Id`, `CreatedAt`) thì đọc
     thẳng qua `room.Match`.
   - Object Domain đồng sở hữu và có thể bị gán vòng qua (`MatchSettings`) thì chuyển thành
     private, chỉ lộ ra qua property suy biến.
2. **Hệ thống Event chuyển xuống Application.** Cách xử lý mọi thứ ở runtime là quy tắc runtime,
   không phải quy tắc nghiệp vụ.
3. **Basis = thứ object đại diện, không phải thứ gây ra nó.**
   - `GameSession` được lưu `Login` (thông tin bổ trợ) nhưng suy biến ra `User`, và `User` là định
     danh của nó (`IEquatable`).
   - Tương tự: `Room` định danh bằng `Match`, `ChannelSession` bằng `IChannel`.
   - Chọn object hay identifier cho linh hoạt: `Room.Beatmap` là object; beatmap trong
     `PlayerStatus` là `Md5?` vì server có thể không có map đó.
4. **Định danh qua basis (`User`), không qua `UserId`.** Id chỉ dùng để tra repository:
   id → repository → `User` → registry.
5. **Tách registry.** Một cho `GameSession`, một cho `IrcSession`, vì hai loại này có thể tồn tại
   song song cho cùng một account. Key là `User`.
6. **Lưu `User` hay `GameSession` trong `Room`?** Hỏi: "user offline rồi vào lại thì thông tin có
   mất không?" Có mất thì lưu `GameSession` (người ngồi slot, host). Không mất thì lưu `User`
   (creator, referees, banned, invited).
7. **Session là đại diện của user đang online.** Việc user *làm được* là method trên session (phía
   chủ động). Việc user *bị làm* là member bị động, do bên ngoài gọi tới. Tương tự cho các session
   khác.
8. **Mọi `ConcurrentDictionary<T, byte>` đổi thành `ConcurrentSet<T>`**
   (`Basil.Domain.Utilities`).
9. **1 thao tác = 1 event, mang toàn bộ hệ quả của thao tác**, để handler xử lý một lần. Ví dụ
   `Resize` phát một event, không phải N event cho từng slot. Event của các thao tác khác nhau
   không chồng thông tin lên nhau.
10. **Event được nhóm theo cây category lồng nhau.** Mục đích là quản lý, biết event thuộc về đâu.
    Tính OOP được tận dụng ở dispatcher: handler đăng ký cho một category (`<T>`) nhận mọi event
    con của nó.
11. **Instance identity.** `IEquatable` lo phần so sánh bằng. Dữ liệu mới từ DB được set thẳng
    vào instance đang sống. Cache chỉ giải phóng một entry sau 5 phút idle **và** khi không còn tham
    chiếu nào.
12. **`Md5` ở mọi chỗ là MD5**, không dùng `string`.
13. **Notification mang tham chiếu**, tên ngắn gọn: `From`, không phải `FromUser`/`FromUserId`.
14. Không đổi các chỗ Application đang gửi thẳng cho client (Messaging, RoomCountdowns,
    ScoreSubmission). Riêng `Invited` đang bị gửi hai lần thì bỏ một lần.
15. Sửa bug `Md5`, `VerifyPassword`, `ContainAdminKey`, null `StoryboardHash` trong commit `fix`
    riêng, làm trước tiên.

---

## 1. Hiện trạng: những gì kế hoạch sửa

Toàn bộ đã kiểm chứng trên `f84f6d74` cộng thay đổi chưa commit của người dùng trong `Room.cs`.

### 1.1 Bản sao thay vì tham chiếu

- `UserSession`: có `UserId` và `ConcurrentDictionary<string, byte> _channels` (tên channel).
- `GameSession`: có `ClientVersion`, `ClientFingerprint`, `LoginTime` (bản sao các field của
  `Login`), `SpectatingUserId`, `ConcurrentDictionary<int, byte> _spectatorIds`, và `RoomId`.
  `RoomId` đang được gán ở bốn nơi khác nhau: `RoomLifecycleCommands.JoinAsync`,
  `Lobby.CloseRoomAsync`, handler `SlotLocked`, `Gateway.DisconnectAsync`.
- `ChannelSession`: có `Name` (string), `ConcurrentDictionary<int, byte> _memberIds`, và `Join`
  bắt caller truyền `IChannel` vào mỗi lần gọi.
- `RoomSlot.User` và `Room.Host` đang là `User`, dù thông tin này mất khi user offline.
- Hệ quả lan ra Services: `usersById.LoadAsync(sender.UserId)` lặp lại ở Messaging, MpCommands
  (2 chỗ), RoomLifecycleCommands (2 chỗ), Gateway. Mọi `NotifyRoom` phải tra
  `players.AllById`. Gateway và Messaging phải load lại `ChatChannel` từ repository.
  `ChatCommands.Roll` và tên phòng mặc định in **id** thay vì tên.
- `IPlayerRegistry.AllById` là `IReadOnlyDictionary<int, UserSession>`, không chứa được
  `IrcSession` và `GameSession` của cùng account. Điều này mâu thuẫn với remark của chính nó.

### 1.2 Event

- Chỉ `Room.cs` phát event, gồm `SettingsChanged`, `RefereeAdded/Removed`, `PlayerInvited`,
  `RoundStarted`, `RoundEnded`. **Không có chỗ nào phát** `PlayerJoined`, `PlayerLeft`,
  `HostChanged`, `PlayerKicked`, `PlayerBanned`, `PlayerUnbanned`, `SlotChanged`, `SlotLocked`,
  `RoomLockChanged`, hay các event từ `PlayerLoaded` đến `PlayerCompleted`, dù handler có sẵn.
  `RoomSlot` và `RoomSlots` không phát được vì `_events` là private.
- `room.Settings.*`, `room.Match.IsVisible`, `room.Slots.Locked` được gán thẳng từ command, không
  phát event.
- Event gộp: `SettingsChanged` gộp 5 loại thay đổi. `SlotChanged` gộp team, mods, status.
  `RoundEnded(bool Aborted)` gộp hai fact khác nhau.
- `Name`, `Password`, `Beatmap` phát event cả khi gán lại đúng giá trị cũ.
- Handler `RoundStarted` tạo `Round` rồi gán `room.CurrentRound`, tức handler đang sửa state của
  nguồn và gán bất đồng bộ. Gọi `!mp start` hai lần liền thì cả hai lần đều thấy
  `InProgress == false`.
- `Invited` bị gửi hai lần: một ở `RefereeCommands.InviteAsync`, một ở handler `PlayerInvited`.
- `Event` và `IEventSource<T>` nằm trong `Basil.Domain/Events`. `UserSession` và `ChannelSession`
  là `IEventSource<Event>` chung chung. `IEventDispatcher` chỉ route theo type cụ thể.
- Remark của `IRoomRegistry.EnterAsync` và `MpCommands` vẫn mô tả "dispose scope thì dispatch
  event", mô hình cũ đã bị bỏ ở `b952986b`.

### 1.3 Md5

- `Md5(string)` lưu `Convert.ToHexString(UTF8(hash))`, tức 64 ký tự là mã hex của chính chuỗi
  hash. Vì vậy `Md5("…") != Md5(bytes)`, và `Submission.Validate` so hash sẽ luôn lệch.
- `ScoreSubmission.SubmitAsync` nhận `(string Hash, string? StoryboardHash)`,
  `(string Md5, string Serial)`, `string clientBeatmapMd5`. `StoryboardHash == null` đi qua
  `implicit Md5(string)` và dẫn tới `ThrowIfNull`, nên mọi lần nộp điểm không có storyboard đều
  ném lỗi (chính là warning CS8604 ở `ScoreSubmission.cs:43`).
- `PlayerStatus.BeatmapMd5` là `string?`.
- `IOsuCalculator.ComputeBeatmapMd5` thừa, vì `new Md5(byte[])` đã làm đúng việc này.

### 1.4 Bug khác trong vùng bị viết lại

- `RoomSlots.Join` trả về slot có người đầu tiên (của **người khác**). Phòng trống thì không ai
  vào được.
- `RoomSlots.Resize` gọi `Math.Clamp(size, 1, playerCount)`, ném lỗi khi phòng trống. Nó còn khoá
  N slot trống đầu tiên, ngược với ý nghĩa của size.
- Indexer của `RoomSlots` đánh số từ 1 (`_slots[index - 1]`), trong khi `RoomSlot.Index` đánh số
  từ 0 và `RoomSlots : IReadOnlyList<RoomSlot>` (contract yêu cầu đánh số từ 0).
- Host không bị xoá khi rời phòng hoặc bị kick.
- Đổi `TeamType` không gán lại hay xoá team của các slot.
- `Room.VerifyPassword` bị đảo điều kiện: `!IsNullOrEmpty(Password) || …` cho **mọi** mật khẩu đi
  qua khi phòng có đặt mật khẩu. Đây là auth bypass.
- `RegisterAttempt.ContainAdminKey` bị đảo điều kiện.

---

## 2. Mô hình đích

Các đoạn code dưới đây là **hình dạng API**, không phải code hoàn chỉnh. XML doc theo rule 6 của
AGENTS.md.

### 2.1 Hệ thống Event (`Basil.Application.Models/Events`)

- Dời `Basil.Domain/Events/Event.cs` và `IEventSource.cs` sang namespace
  `Basil.Application.Models.Events`, xoá thư mục Domain tương ứng.
- Đổi tên `IDomainEventHandler<TEvent>` thành `IEventHandler<TEvent>`
  (`Basil.Application.Contracts/Events`).
- **Contract `IEventDispatcher`** (chỉ sửa doc, chưa có implementation trong phạm vi): một event
  được giao cho mọi `IEventHandler<T>` có `T` là type cụ thể của event **hoặc bất kỳ category
  nào trên chuỗi kế thừa của nó**, lần lượt từ `Event` trở xuống.
- Sửa remark `IRoomRegistry.EnterAsync` và `MpCommands`: scope chỉ cấp quyền sửa độc quyền; việc
  dispatch do dispatcher đọc event source đảm nhiệm.
- Sửa remark `ICache`: một entry chỉ được giải phóng sau 5 phút idle **và** khi instance không còn
  được tham chiếu, để luồng cập nhật luôn set vào đúng instance đang sống.

**Cây category.** Mọi node không phải lá là `abstract record`. Tham số của base suy biến từ tham
số của lớp con khi có thể, ví dụ `RoomSlotEvent(RoomSlot Slot) : RoomSlotsEvent(Slot.Slots.Room)`.

```text
Event
├─ RoomEvent(Room Room)                                   source: Room
│  ├─ RoomSettingsEvent
│  │  ├─ RoomNameChanged(string Name)
│  │  ├─ RoomVisibilityChanged(bool IsVisible)
│  │  ├─ RoomPasswordChanged(string Password)
│  │  ├─ BeatmapChanged(Beatmap? Beatmap)
│  │  ├─ GameModeChanged(GameMode Mode)
│  │  ├─ ModsChanged(GameMods Mods)
│  │  ├─ FreemodsChanged(bool Enabled)
│  │  ├─ TeamTypeChanged(GameTeamType TeamType)          (gồm việc gán lại team cho các slot)
│  │  └─ WinConditionChanged(GameWinCondition WinCondition)
│  ├─ RoomAuthorityEvent
│  │  ├─ HostChanged(GameSession? Host)
│  │  ├─ RefereeAdded(User Referee)
│  │  └─ RefereeRemoved(User Referee)
│  ├─ RoomAccessEvent
│  │  ├─ PlayerBanned(User Player, RoomSlot? Vacated)     (gồm việc đá khỏi slot nếu đang ngồi)
│  │  ├─ PlayerUnbanned(User Player)
│  │  └─ PlayerInvited(User Player)
│  ├─ RoomMembershipEvent
│  │  ├─ PlayerJoined(GameSession Player, RoomSlot Slot)
│  │  ├─ PlayerLeft(GameSession Player, RoomSlot Slot)     (gồm việc xoá host nếu cần)
│  │  ├─ PlayerKicked(GameSession Player, RoomSlot Slot)   (gồm việc xoá host nếu cần)
│  │  └─ PlayerMoved(GameSession Player, RoomSlot From, RoomSlot To)
│  ├─ RoomSlotsEvent
│  │  ├─ RoomResized(int Size)
│  │  ├─ RoomLockChanged(bool Locked)
│  │  └─ RoomSlotEvent(RoomSlot Slot)
│  │     ├─ SlotLockChanged(bool Locked, GameSession? Evicted)
│  │     ├─ SlotTeamChanged(GameTeam? Team)
│  │     ├─ SlotModsChanged(GameMods? Mods)
│  │     └─ SlotStatusChanged(RoomSlotStatus? Status)
│  ├─ RoundEvent(Round Round)
│  │  ├─ RoundStarted
│  │  ├─ RoundAborted
│  │  └─ RoundCompleted, PlayerLoaded(RoomSlot), AllPlayersLoaded, PlayerSkipped(RoomSlot),
│  │     AllPlayersSkipped, PlayerFailed(RoomSlot), PlayerCompleted(RoomSlot)   (*)
│  └─ RoomClosed(IReadOnlyList<GameSession> Evicted)
├─ ChannelEvent(ChannelSession Channel)                   source: ChannelSession
│  └─ ChannelMembershipEvent
│     ├─ MemberJoined(UserSession Member)
│     └─ MemberParted(UserSession Member)
└─ SessionEvent(UserSession Session)                      source: UserSession
   ├─ PresenceEvent
   │  └─ StatusChanged(PlayerStatus Status)               (chỉ GameSession)
   └─ SpectatorEvent                                      (ghi vào session của host)
      ├─ SpectatorAdded(GameSession Spectator)
      └─ SpectatorRemoved(GameSession Spectator)
```

(*) Chỉ giữ khai báo và handler, **chưa có điểm phát**. Mô hình chưa có state Loaded/Failed; việc
này để lại cho lúc port packet handler của lượt chơi.

Các event bị xoá: `SettingsChanged`, `SlotChanged`, `SlotLocked`, `RoundEnded`, `SpectateStarted`,
`SpectateStopped`, `ChannelJoined`, `ChannelParted` (hai event cuối đổi tên thành
`MemberJoined`/`MemberParted`, để không trùng tên với Notification cùng tên).

Lưu ý khi viết: record lồng nhau có tham số positional trùng tên với property của base nhưng khác
kiểu (ví dụ `Session` là `GameSession` so với `UserSession`) sẽ **không** sinh property mới. Hãy
đặt tên khác hoặc dùng property kiểu hẹp hơn có tên riêng.

### 2.2 Session

```csharp
public abstract class UserSession : IEventSource<SessionEvent>, IEquatable<UserSession>
{
    private readonly ConcurrentSet<ChannelSession> _channels = [];
    public abstract User User { get; }                    // basis, định danh
    public abstract DateTimeOffset LoginTime { get; }
    public required IClientConnection Connection { get; init; }
    public DateTimeOffset LastActiveAt { get; set; }
    public string? AwayMessage { get; set; }
    public IReadOnlySet<ChannelSession> Channels => _channels;
    public ChannelReader<SessionEvent> Events { get; }

    // chủ động: user tham gia / rời channel. Sửa cả hai phía; event ghi vào ChannelSession.
    public void Join(ChannelSession channel);             // ném lỗi nếu User không có quyền đọc
    public void Part(ChannelSession channel);
    // bị động
    public void Notify(Notification notification);
    // Equals: cùng kiểu cụ thể và cùng User
}

public sealed class GameSession : UserSession
{
    private readonly ConcurrentSet<GameSession> _spectators = [];
    public required Login Login { get; init; }            // bổ trợ; basis vẫn là User
    public override User User => Login.User;
    public override DateTimeOffset LoginTime => Login.OccurredAt;
    public ClientVersion ClientVersion => Login.Version;
    public ClientFingerprint ClientFingerprint => Login.Fingerprint;
    public IPAddress Ip => Login.Ip;
    public int UtcOffset { get; init; }
    public PresenceVisibility PresenceVisibility { get; set; }
    public PlayerStatus Status { get; set; }              // phát StatusChanged nếu giá trị đổi
    public GameSession? Spectating { get; private set; }
    public IReadOnlySet<GameSession> Spectators => _spectators;
    public RoomSlot? Slot { get; internal set; }          // chỉ RoomSlot gán
    public Room? Room => Slot?.Slots.Room;                // suy biến

    // chủ động
    public void Spectate(GameSession host);               // phát SpectatorAdded vào host
    public void StopSpectating();                         // phát SpectatorRemoved vào host
    public RoomSlot? JoinRoom(Room room);                 // null khi phòng đầy; phát PlayerJoined
    public void LeaveRoom();                              // phát PlayerLeft
}

public sealed class IrcSession(User user, DateTimeOffset loginTime) : UserSession
{
    public override User User => user;
    public override DateTimeOffset LoginTime => loginTime;
}

public sealed class ChannelSession : IEventSource<ChannelEvent>, IEquatable<ChannelSession>
{
    private readonly ConcurrentSet<UserSession> _members = [];
    public required IChannel Channel { get; init; }       // basis, định danh
    public string Name => Channel.Name;                   // suy biến
    public IReadOnlySet<UserSession> Members => _members;
    public bool CanRead(User user);
    public bool CanWrite(User user);
    internal void Add(UserSession member);                // bị động; phát MemberJoined
    internal void Remove(UserSession member);             // bị động; phát MemberParted
}
```

Ghi chú:

- `private protected Record(...)` hiện có của `UserSession` được giữ (hoặc đổi thành `internal`)
  để `GameSession` ghi được event vào source của host.
- `Spectate` ném `InvalidOperationException` khi tự spectate chính mình hoặc đang spectate người
  khác, giữ nguyên luật hiện tại.
- `JoinRoom` ném `InvalidOperationException` nếu session đang ngồi ở phòng **khác**, và trả về slot
  hiện có nếu đang ngồi ở chính phòng đó. Ném `InvalidOperationException` nếu user bị ban (luật
  của `RoomSlots`).

**Registry** (`Basil.Application.Contracts/Registries`): thay `IPlayerRegistry` bằng

```csharp
public interface ISessionRegistry<TSession> where TSession : UserSession
{
    IReadOnlyDictionary<User, TSession> AllByUser { get; }
    bool TryAdd(TSession session);                         // false nếu User đã có session loại này
    void Remove(TSession session);
}
```

Registry này được đăng ký hai lần: `ISessionRegistry<GameSession>` và `ISessionRegistry<IrcSession>`.
`FindByName` bị bỏ; tra theo tên đi qua `usersByName` rồi tới `AllByUser`. `IRoomRegistry`
(key = room id client gửi lên) và `IChannelRegistry` (key = tên channel) giữ nguyên key.

### 2.3 Room, RoomSlots, RoomSlot

```csharp
public sealed class Room : IEventSource<RoomEvent>, IEquatable<Room>   // Equals theo Match
{
    public required Match Match { get; init; }            // basis, định danh
    public MatchSettings Settings { private get; init; }  // đồng sở hữu → ẩn
    public required int Id { get; init; }                 // id runtime mà client dùng
    public ChannelSession Channel { get; }                // = new ChannelSession { Channel = new RoomChannel(this) }
    public RoomSlots Slots { get; }

    // suy biến sang Match; setter phát event, chỉ khi giá trị đổi
    public string Name { get => Match.Name; set; }         // RoomNameChanged
    public bool IsVisible { get => Match.IsVisible; set; } // RoomVisibilityChanged
    public User? Creator => Match.Creator;
    // suy biến sang Settings
    public GameMode Mode { get; set; }                     // GameModeChanged
    public GameMods Mods { get; set; }                     // ModsChanged
    public bool Freemods { get; set; }                     // FreemodsChanged
    public GameTeamType TeamType { get; set; }             // TeamTypeChanged, gán lại/xoá team các slot
    public GameWinCondition WinCondition { get; set; }     // WinConditionChanged

    // state runtime riêng của Room
    public string Password { private get; set; }           // RoomPasswordChanged
    public Beatmap? Beatmap { get; set; }                  // BeatmapChanged
    public GameSession? Host { get; set; }                 // HostChanged; phải đang ngồi trong phòng
    public Round? CurrentRound { get; private set; }
    public bool InProgress => CurrentRound is not null;
    public IReadOnlySet<User> Referees { get; }           // chỉ ghi qua Add/RemoveReferee
    public IReadOnlySet<User> Banned { get; }
    public IReadOnlySet<User> Invited { get; }

    public bool IsReferee(User user);                      // Referees ∪ Creator
    public bool VerifyPassword(string password);           // đã sửa đảo điều kiện
    public void Kick(GameSession player);                  // PlayerKicked
    public void Ban(User player);                          // PlayerBanned(…, Vacated)
    public void Unban(User player);                        // PlayerUnbanned
    public void Invite(User player);                       // PlayerInvited
    public void AddReferee(User referee);                  // RefereeAdded
    public void RemoveReferee(User referee);               // RefereeRemoved
    public Round Start(int roundId);                       // RoundStarted
    public void Abort();                                   // RoundAborted
    public IReadOnlyList<GameSession> Close();             // RoomClosed
    internal void Emit(RoomEvent @event);                  // chỉ dùng trong Application.Models
}
```

- `Room.Name` và `Room.Creator` là property suy biến. `IsCreator` bị bỏ; nơi gọi dùng
  `user.Equals(room.Creator)`. `IsReferee` giữ lại vì nó là luật của Room.
- **Domain:** thêm `public User? Creator { get; init; }` vào `Match` (creator có quyền vĩnh viễn,
  nên thuộc basis). `Lobby.CreateRoomAsync` truyền creator vào `Match`.
- `Start(roundId)`:
  - Ném lỗi nếu `InProgress` hoặc `Beatmap is null`.
  - Tạo `Round` từ `Match`, `Beatmap.Hash`, một bản snapshot của `Settings` (tạo `MatchSettings`
    mới bằng giá trị), và `OccurredAt = now`.
  - Gán `CurrentRound`. Chuyển mọi slot có người và không ở `NoMap` sang `Playing`.
  - Phát **một** `RoundStarted`.
- `Abort()`:
  - Ném lỗi nếu không có round đang chạy.
  - Gán `EndedAt` và `Aborted = true`. Slot đang `Playing`/`Complete` về `NotReady`. Xoá
    `CurrentRound`.
  - Phát `RoundAborted`.
- `Close()`:
  - Đá mọi người ra (đi qua đường im lặng), gán `Match.EndedAt`.
  - Phát `RoomClosed(evicted)`, trả về danh sách người bị đá.

```csharp
public sealed class RoomSlots : IReadOnlyList<RoomSlot>
{
    public const int MaxSlotCount = 16;
    public readonly Room Room;
    public bool Locked { get; set; }                      // RoomLockChanged
    public RoomSlot this[int index] { get; }              // đánh số từ 0, theo IReadOnlyList
    public RoomSlot? Find(User user);                     // so sánh slot.Session.User
    public RoomSlot? Find(GameSession session);
    public void Resize(int size);                         // RoomResized (một event)
    public static void Move(RoomSlot from, RoomSlot to);  // PlayerMoved
    internal RoomSlot? Seat(GameSession session);         // bị động, do GameSession.JoinRoom gọi; PlayerJoined
    internal RoomSlot Vacate(GameSession session);        // im lặng; người gọi phát event của thao tác
}

public sealed class RoomSlot
{
    public readonly RoomSlots Slots;
    public readonly int Index;                             // đánh số từ 0, khớp wire
    public GameSession? Session { get; }                   // gán qua Occupy/Clear để giữ GameSession.Slot khớp
    public bool Locked { get; set; }                       // SlotLockChanged(Locked, Evicted)
    public RoomSlotStatus? Status { get; set; }            // SlotStatusChanged
    public GameTeam? Team { get; set; }                    // SlotTeamChanged
    public GameMods? Mods { get; set; }                    // SlotModsChanged
    public bool? IntroSkipped { get; set; }                // chưa có event (thuộc lượt chơi)
}
```

- `Seat`:
  - Chọn slot trống, không khoá, có index nhỏ nhất.
  - Nếu phòng chia team thì gán team bên đang ít người hơn.
  - Gán `session.Slot`. Phát `PlayerJoined`.
- `Resize(size)`, theo bancho.py:
  - Mở khoá slot `[0, size)`.
  - Khoá slot **trống** trong `[size, 16)`. Slot có người thì không đụng tới.
  - Phát một `RoomResized(size)`.
- Setter `RoomSlot.Locked`: khoá slot có người sẽ đá người đó ra. Phát
  `SlotLockChanged(true, evicted)` và không phát thêm event membership nào.
- **Đường im lặng** là các primitive internal, không phát event: `Occupy`, `Clear`, và các biến
  thể `SetLocked`/`SetTeam`/`SetStatus` dùng nội bộ. Thao tác gộp (`Ban` gọi `Vacate`, `Resize`
  gọi `SetLocked`, setter `TeamType` gọi `SetTeam`, `Start`/`Abort` gọi `SetStatus`, `Close` gọi
  `Vacate`) chỉ đi qua đường im lặng rồi phát **một** event của thao tác ngoài cùng.
- Xoá host: khi người đang là host rời slot (qua `LeaveRoom`, `Kick`, `Ban`, bị đá do khoá slot,
  hoặc `Close`) thì `Host = null` qua đường im lặng. Đây là hệ quả nằm trong event của thao tác đó.

### 2.4 Bảng: thao tác → event → nơi phát

| Thao tác (public) | Event duy nhất | Phát tại |
|---|---|---|
| `room.Name = x` | `RoomNameChanged` | setter `Room.Name` |
| `room.IsVisible = x` | `RoomVisibilityChanged` | setter `Room.IsVisible` |
| `room.Password = x` | `RoomPasswordChanged` | setter `Room.Password` |
| `room.Beatmap = x` | `BeatmapChanged` | setter `Room.Beatmap` |
| `room.Mode/Mods/Freemods/WinCondition = x` | `GameModeChanged`/`ModsChanged`/`FreemodsChanged`/`WinConditionChanged` | setter tương ứng trên `Room` |
| `room.TeamType = x` | `TeamTypeChanged` | setter `Room.TeamType` |
| `room.Host = x` | `HostChanged` | setter `Room.Host` |
| `room.AddReferee/RemoveReferee` | `RefereeAdded`/`RefereeRemoved` | method tương ứng |
| `room.Ban/Unban/Invite` | `PlayerBanned`/`PlayerUnbanned`/`PlayerInvited` | method tương ứng |
| `room.Kick(session)` | `PlayerKicked` | `Room.Kick` |
| `session.JoinRoom(room)` | `PlayerJoined` | `RoomSlots.Seat` |
| `session.LeaveRoom()` | `PlayerLeft` | `GameSession.LeaveRoom` |
| `RoomSlots.Move(a, b)` | `PlayerMoved` | `RoomSlots.Move` |
| `room.Slots.Resize(n)` | `RoomResized` | `RoomSlots.Resize` |
| `room.Slots.Locked = x` | `RoomLockChanged` | setter `RoomSlots.Locked` |
| `slot.Locked/Team/Mods/Status = x` | `SlotLockChanged`/`SlotTeamChanged`/`SlotModsChanged`/`SlotStatusChanged` | setter tương ứng |
| `room.Start(id)` / `room.Abort()` / `room.Close()` | `RoundStarted` / `RoundAborted` / `RoomClosed` | method tương ứng |
| `session.Join/Part(channel)` | `MemberJoined`/`MemberParted` | `ChannelSession.Add/Remove` |
| `game.Status = x` | `StatusChanged` | setter `GameSession.Status` |
| `game.Spectate(host)` / `game.StopSpectating()` | `SpectatorAdded`/`SpectatorRemoved` (vào host) | method tương ứng |

Mọi setter chỉ phát event khi giá trị thật sự đổi.

### 2.5 Handler (giữ nguyên hành vi hiện tại, đăng ký theo category)

| Handler, đăng ký cho | Phản ứng |
|---|---|
| `RoomEventHandlers : IEventHandler<RoomSettingsEvent>` | `RoomUpdated` tới mọi `slot.Session` |
| `… : IEventHandler<RoomSlotsEvent>` | `RoomUpdated` tới cả phòng; nếu có `SlotLockChanged.Evicted` thì gửi thêm `RoomUpdated` cho người bị đá |
| `… : IEventHandler<RoomMembershipEvent>` | Ghi `MatchEvent` (Joined, Left, Kicked; không ghi Moved). Gửi `RoomUpdated` tới phòng. Với `PlayerKicked`, gửi thêm cho người bị đá. Với `PlayerLeft`, nếu phòng trống thì `lobby.CloseRoomAsync` |
| `… : IEventHandler<RoomAuthorityEvent>` | Ghi `MatchEvent` (HostGranted, RefAdded, RefRemoved). `HostChanged` gửi `HostTransferred`; `Referee*` gửi `RoomUpdated` |
| `… : IEventHandler<RoomAccessEvent>` | `PlayerBanned` gửi `RoomUpdated` (và cho người bị đá nếu `Vacated`). `PlayerInvited` gửi `Invited(room, From: room.Creator)` cho session game của người được mời. `PlayerUnbanned`: không làm gì |
| `… : IEventHandler<RoomClosed>` | Gửi `Notification.RoomClosed(room)` cho mỗi người bị đá |
| `RoundEventHandlers : IEventHandler<RoundEvent>` | `RoundStarted`: lưu round, gửi `Notification.RoundStarted`. `RoundAborted`: lưu round, gửi `RoundAborted`. Các event (*): giữ nguyên phản ứng hiện tại |
| `ChannelEventHandlers : IEventHandler<ChannelMembershipEvent>` | Gửi `ChannelJoined`/`ChannelParted` cho member; gửi `ChannelInfoChanged` cho các member còn lại |
| `SessionEventHandlers : IEventHandler<PresenceEvent>` | Gửi `PresenceChanged` cho mọi `GameSession` (xem mục 6, rủi ro 2) |
| `… : IEventHandler<SpectatorEvent>` | Gửi `SpectatorJoined`/`SpectatorLeft` cho host |

`DependencyInjection` chỉ còn khoảng 10 dòng đăng ký, mỗi dòng cho một category.

### 2.6 Md5

- `Md5(string)`: `HashValue = hash.ToLowerInvariant()`, sau khi đã kiểm tra hợp lệ như hiện tại.
- `PlayerStatus.BeatmapMd5: string?` đổi thành `BeatmapHash: Md5?`, đồng nhất với `Beatmap.Hash`,
  `Round.BeatmapHash`, `Score.BeatmapHash`.
- `ScoreSubmission.SubmitAsync(..., (Md5 Hash, Md5? StoryboardHash) beatmap, ...,
  (Md5 Hash, string Serial) clientFingerprint, string clientVersionDate, Md5 clientBeatmapHash, ...)`.
- Xoá `IOsuCalculator.ComputeBeatmapMd5`; `BeatmapCatalog` dùng `new Md5(file.Content)`.

### 2.7 Notification mang tham chiếu

| Hiện tại | Đích |
|---|---|
| `ChatNotification(int FromUserId, string Target, string Text)` | `ChatNotification(User From, string Target, string Text)` |
| `Invited(Room Room, int FromUserId)` | `Invited(Room Room, User? From)` |
| `SpectatorJoined(int UserId)` / `SpectatorLeft(int UserId)` | `(GameSession Spectator)` |
| `PlayerOffline(int UserId)` | `PlayerOffline(User User)` |
| `RoomClosed(int RoomId)` | `RoomClosed(Room Room)` |
| `ChannelJoined/ChannelParted/ChannelInfoChanged(string Channel)` | `(ChannelSession Channel)` |
| `PlayerFailed(int Slot)` / `PlayerSkipped(int Slot)` | `(RoomSlot Slot)` |
| `NotificationRefused(string Target, …)` | **giữ `string`**: target có thể là tên không tồn tại |

- `Messaging` load `User` của BasilBot (`SystemUserIds.BasilBot`) qua `usersById` khi cần `From`.
- Tin nhắn away hiện gửi `Target = from.UserId.ToString()`; sửa thành `from.User.Name`.

---

## 3. Các bước thực thi

**Quy tắc chung cho mọi bước:**

- Build: `dotnet build src/Basil.Application.Services/Basil.Application.Services.csproj -nologo`
  (lệnh này build cả Domain, Models, Contracts).
- Đạt khi: **0 lỗi** và **≤ 20 warning** (baseline trên `f84f6d74`). Không có warning mới nào nằm
  trong file mình đã sửa.
- Mỗi bước là một commit conventional, chỉ commit local, dòng cuối có attribution theo session.
- Stage theo file hoặc theo hunk. **Không** stage thay đổi `Unban` chưa commit của người dùng
  trong `Room.cs`, trừ khi người dùng bảo khác (xem mục 6).
- Giữ nguyên thứ tự: mỗi bước build xanh trước khi sang bước sau.

### Bước 1 — `fix`: bug chặn đường (hai commit)

1. `fix(domain): store Md5 string values as lowercase hex`
   - Sửa `Basil.Domain/Utilities/Md5.cs` theo mục 2.6.
2. `fix(app): repair inverted room password and admin-key checks, and null storyboard hash`
   - `Room.VerifyPassword`: `string.IsNullOrEmpty(Password) || Password == password`.
   - `RegisterAttempt.ContainAdminKey`: `!string.IsNullOrEmpty(AdminKey)`.
   - `ScoreSubmission.SubmitAsync`: đổi tuple beatmap thành `(Md5 Hash, Md5? StoryboardHash)` để
     null không đi qua `implicit Md5(string)` nữa. Phần đổi các tham số còn lại sang `Md5` để dành
     cho bước 6.
- Kiểm tra: build xanh; warning CS8604 ở `ScoreSubmission.cs` biến mất.

### Bước 2 — `refactor(app)`: nền Event

`refactor(app): move eventing into Application and group events by category`

- Dời `Event.cs` và `IEventSource.cs` sang `Basil.Application.Models/Events`, xoá
  `Basil.Domain/Events`.
- Đổi tên `IDomainEventHandler` thành `IEventHandler` (file và mọi nơi dùng).
- Thêm các category base (`RoomEvent` và các nhánh con, `ChannelEvent`, `SessionEvent` và nhánh
  con) theo mục 2.1. Chuyển các event **hiện có** vào đúng nhánh, **chưa** tách hay thêm event
  mới.
- `UserSession : IEventSource<SessionEvent>`, `ChannelSession : IEventSource<ChannelEvent>`.
- Sửa doc: `IEventDispatcher` (dispatch theo chuỗi kế thừa), `IRoomRegistry.EnterAsync`,
  `MpCommands` remark, `ICache` remark.
- Kiểm tra: build xanh. `grep -r "Basil.Domain.Events\|IDomainEventHandler" src/Basil.Domain
  src/Basil.Application.*` trả về rỗng. Hành vi không đổi.

### Bước 3 — `refactor(app)`: Session và registry

`refactor(app): make sessions represent their user through references and ConcurrentSet`

- **Models:** `UserSession`, `GameSession`, `IrcSession`, `ChannelSession` theo mục 2.2.
  - `ChangeStatus` đổi thành setter `Status`. `StartSpectating(host)`/`StopSpectating(host)` đổi
    thành `Spectate(host)`/`StopSpectating()`.
  - `SessionEvents.cs` theo cây mới: `StatusChanged`, `SpectatorAdded`, `SpectatorRemoved`,
    `MemberJoined`, `MemberParted`.
  - Tạm thời `GameSession.Slot`/`Room` chỉ khai báo; việc nối với Room làm ở bước 4.
    `JoinRoom`/`LeaveRoom` cũng làm ở bước 4.
- **Contracts:** thêm `ISessionRegistry<TSession>`, xoá `IPlayerRegistry`.
- **Services** (sửa nơi gọi):
  - `Gateway`:
    - `ConnectAsync` thêm tham số `IPAddress ip` để dựng `Login`. Auto-join duyệt các
      `ChannelSession` trong registry theo `Channel.AutoJoin` và `CanRead(user)`, rồi gọi
      `session.Join(channelSession)`. Bỏ dependency `IRepository<string, ChatChannel>`.
    - `DisconnectAsync` dùng `StopSpectating()`, duyệt `Spectators.ToArray()`, duyệt
      `Channels.ToArray()` rồi `Part`. Tìm đúng registry theo kiểu session.
  - `Messaging`:
    - Dùng `from.User` thay cho việc load lại sender.
    - Membership kiểm tra bằng `membership.Members.Contains(from)` và `CanWrite(from.User)`. Bỏ
      việc load `ChatChannel` từ repository.
    - Người nhận PM: `usersByName` → `games.AllByUser` (giữ luật hiện tại: chỉ giao cho
      `GameSession`).
  - `Lobby`: đăng ký `ChannelSession` của phòng, part các member bằng
    `member.Part(channelSession)`.
  - `MpCommands`, `RoomLifecycleCommands`, `RefereeCommands`: dùng `sender.User`, bỏ
    `usersById.LoadAsync(sender.UserId)`. `RefereeCommands.InviteAsync` kiểm tra
    `games.AllByUser.ContainsKey(target)`.
  - `ChatCommands.Roll` và tên phòng mặc định ở `MakeAsync`: in `sender.User.Name`.
  - Handlers: `ChannelEventHandlers` duyệt `channel.Members`. `SessionEventHandlers` dùng registry
    game.
- Kiểm tra: build xanh. Grep trong `src/Basil.Application.*` không còn
  `ConcurrentDictionary<.*, byte>`, `IPlayerRegistry`, `FindByName`, `\.UserId\b` (ngoại lệ duy
  nhất: nơi truyền id vào repository), `MemberIds`, `SpectatorIds`, `SpectatingUserId`.

### Bước 4 — `refactor(domain,app)`: Room

`refactor(app): derive Room's shared state from Match and seat game sessions in slots`

- **Domain:** thêm `Match.Creator`.
- **Models:** `Room`, `RoomSlots`, `RoomSlot` theo mục 2.3. Ở bước này mới **nối dây**:
  - Suy biến `Name`, `IsVisible`, `Creator`, `Mode`, `Mods`, `Freemods`, `TeamType`,
    `WinCondition`. `Settings` chuyển thành private.
  - `IEquatable<Room>` theo `Match`.
  - Slot và Host chuyển sang `GameSession`; `GameSession.Slot` do `RoomSlot` gán.
    `GameSession.JoinRoom`/`LeaveRoom`.
  - Sửa `Seat` (thay cho `Join`), `Resize`, indexer đánh số từ 0, và việc xoá host.
  - Setter `TeamType` gán lại hoặc xoá team.
  - `Start(roundId)`, `Abort()`, `Close()`.
  - `Room.Channel` là `ChannelSession`; `RoomChannel` giữ nguyên, chỉ được bọc bên trong.
  - Bỏ `IsCreator`.
- **Services:**
  - `MatchFlowCommands`:
    - Gán `room.Mods`, `Freemods`, `TeamType`, `WinCondition`.
    - `StartAsync` cấp id qua `IIdAllocator<Round>` rồi gọi `room.Start(id)`. Kiểm tra trước:
      `InProgress` thì trả `MatchAlreadyInProgress`; không có beatmap thì trả reply mới
      `NoBeatmapSelected` (mục 6).
  - `RoomCountdowns`: `StartMatchAsync` cũng cấp id như trên. `NotifyRoom` gửi thẳng qua
    `slot.Session?.Notify`.
  - `RoomSettingsCommands`: gán `room.IsVisible`, đọc `room.Name`, `room.Mods`, …
  - `SlotCommands`:
    - `Find(user)?.Session` rồi `room.Host = session` / `room.Kick(session)`.
    - `!mp move <1-16>` đổi thành `room.Slots[n - 1]`.
  - `RoomLifecycleCommands.JoinAsync`:
    - `sender is not GameSession` thì trả reply mới `JoinRequiresClient`.
    - Kiểm tra trước: banned, private, mật khẩu (giữ reply cũ), đang ở phòng khác (reply mới
      `AlreadyInAnotherRoom`).
    - `game.JoinRoom(room)`; trả về null thì `MatchIsFull`.
    - `sender.Join(room.Channel)`.
  - `Lobby.CreateRoomAsync` dựng `Match { Creator = creator }`. `CloseRoomAsync` gọi
    `room.Close()`, lưu `Match`, xoá phòng khỏi registry, part các member của `room.Channel`.
  - `MpCommands`: `sender is not GameSession { Room: { } room }` thì trả `NotInARoom`.
  - Hai handler class hiện có: bỏ tra registry ở `NotifyRoom`. Bỏ việc sửa `RoomId` trong handler
    `SlotLocked`. Handler `RoundStarted` không tạo `Round` nữa, chỉ lưu `domainEvent.Round`.
- Kiểm tra: build xanh. Grep không còn `\.RoomId\b`, `IsCreator`, `room.Settings.`,
  `room.Match.Name =`, `room.Match.IsVisible =`, `_slots\[index - 1\]`.

### Bước 5 — `refactor(app)`: Event theo thao tác và handler theo category

`refactor(app): emit one event per room/session operation and route handlers by category`

- **Models:** hoàn tất cây event mục 2.1. Xoá `SettingsChanged`, `SlotChanged`, `SlotLocked`,
  `RoundEnded`. Phát event đúng tại các chỗ trong bảng 2.4. Thêm kiểm tra bằng giá trị cũ trước khi
  phát. Thao tác gộp đi qua đường im lặng.
- **Services:**
  - Gộp `RoomMatchEventHandlers` và `RoomMembershipEventHandlers` thành `RoomEventHandlers` và
    `RoundEventHandlers`, theo bảng 2.5.
  - `DependencyInjection` đăng ký theo category.
  - Bỏ lệnh `Notify(new Invited(...))` trực tiếp trong `RefereeCommands.InviteAsync`.
- Kiểm tra: build xanh. Với mỗi event lá trong 2.1 không có dấu (*), `grep -rn "new <Tên>("`
  trong `src/Basil.Application.*` trả về **đúng** chỗ phát ghi trong bảng 2.4 (bỏ qua file khai
  báo). Grep không còn `SettingsChanged`, `SlotChanged`, `SlotLocked`, `RoundEnded`,
  `CurrentRound =` ngoài `Room.cs`.

### Bước 6 — `refactor(app)`: Md5

`refactor(app): type every MD5 as Md5`

- `PlayerStatus.BeatmapHash: Md5?`. Đổi các tham số còn lại của `ScoreSubmission` sang `Md5`. Xoá
  `IOsuCalculator.ComputeBeatmapMd5`; `BeatmapCatalog` dùng `new Md5(file.Content)`.
- Kiểm tra: build xanh. `grep -rniE "string\??\s+\w*(md5|hash)\b"` trong `src/Basil.Application.*`
  và `src/Basil.Domain` chỉ còn `IPasswordHasher` và `AdminKeySettings.Hash` (hai chỗ này là hash
  mật khẩu hoặc key, không phải MD5).

### Bước 7 — `refactor(app)`: Notification mang tham chiếu

`refactor(app): carry references instead of ids in notifications`

- Đổi theo bảng 2.7 và sửa mọi nơi tạo ra các notification đó.
- Kiểm tra: build xanh. Grep `Notifications/*.cs` không còn tham số `int …Id`, trừ những chỗ ghi
  "giữ" trong bảng.

---

## 4. Kiểm chứng tổng sau bước 7

1. Build 4 project: 0 lỗi, ≤ 20 warning.
2. Chạy lại toàn bộ grep của bước 2–7.
3. Rà `git diff f84f6d74..HEAD --stat` và bảo đảm chỉ có thay đổi trong `src/Basil.Domain` và
   `src/Basil.Application.*`, cùng file plan này.
4. Tự rà theo checklist:
   - Mỗi thao tác public ở bảng 2.4 phát đúng một event.
   - Không có setter nào phát event khi giá trị không đổi.
   - Không có `ConcurrentDictionary<*, byte>`.
   - Không còn property runtime nào sao chép field của `Match`/`Login`/`IChannel`.
5. **Giới hạn:** không có test tự động nào chạy được trên code mới, vì mọi test project đang trỏ
   tới project đã xoá. Hành vi chỉ được kiểm bằng build, grep và review.

---

## 5. Ngoài phạm vi / để lại

- Infrastructure, `Host.*`, test project, implementation của dispatcher và registry.
- Điểm phát cho các event lượt chơi (*): mô hình chưa có state Loaded/Failed/Completed.
- Messaging, RoomCountdowns, ScoreSubmission vẫn gửi thẳng cho client (quyết định 0.2 #14).
- `!mp` chỉ phân giải phòng qua `GameSession.Room`. Referee dùng IRC mà không ngồi slot vẫn nhận
  `NotInARoom`, giống hiện tại. Việc phân giải theo channel `#mp_…` để lại cho sau.
- `implicit operator Md5(string)` vẫn giữ. Bỏ nó sẽ buộc gõ kiểu tường minh, nhưng người dùng chưa
  yêu cầu.
- Cập nhật tài liệu `docs/for-developers/*` (architecture, multiplayer) để lại cho đợt đóng
  migration.

---

## 6. Rủi ro và điểm cần người dùng duyệt trước khi chạy

1. **Reply mới** (thay đổi contract hiển thị cho người dùng, rule 9 của AGENTS.md):
   `MpReplies.JoinRequiresClient`, `MpReplies.NoBeatmapSelected`,
   `MpReplies.AlreadyInAnotherRoom`. File locale (`mp.en.json`) đã bị xoá khỏi repo từ `fc1bd033`,
   nên hiện chỉ thêm được hằng số key; nội dung text cần người dùng duyệt khi locale được đặt lại.
2. **`PresenceChanged`** hiện được gửi cho *mọi* session, kể cả IRC. Theo kế hoạch chỉ gửi cho
   registry game, vì presence là khái niệm của osu! client. Đây là thay đổi hành vi nhỏ; nếu muốn
   giữ nguyên thì gửi cho cả hai registry.
3. **`Room.cs` có thay đổi chưa commit của người dùng** (`Unban`). Bước 1 và 4 đều sửa file này.
   Mặc định: stage theo hunk để commit của mình không chứa thay đổi đó. Người dùng có thể chọn
   commit riêng thay đổi đó trước.
4. **Dispatch theo chuỗi kế thừa** hiện chỉ là contract. Hành vi thật phụ thuộc implementation
   dispatcher ở Infrastructure, nằm ngoài phạm vi.
5. **`Start` chuyển slot sang `Playing` và `Abort` trả slot về `NotReady`** là luật mới được thêm
   vào `Room`, dựa trên hành vi osu!/bancho.py. Hiện tại các thao tác này không đụng tới trạng
   thái slot.
6. **Thay đổi tài liệu chưa commit.** Gồm: xoá 48 file plan cũ, đổi `CLAUDE.md` thành `AGENTS.md`
   (thêm `CLAUDE.md` một dòng `@AGENTS.md`), sửa link trong `CONTRIBUTING.md` và `docs/`, và thêm
   chính file plan này. Việc xoá và đổi tên **đã được stage**, nên một lệnh `git commit` bình thường
   ở bước 1 sẽ gộp chúng vào commit `fix(domain)`, và kiểm tra số 3 ở mục 4 sẽ sai. Trước bước 1,
   chọn một trong hai:
   - commit riêng trước: `docs: move agent instructions to AGENTS.md and replace superseded plans`;
   - hoặc `git restore --staged plans AGENTS.md CLAUDE.md`, giữ các thay đổi chỉ trong working
     tree.
