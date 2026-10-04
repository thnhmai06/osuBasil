# Kế hoạch: tách dữ liệu khỏi định danh (wrapper), bỏ `IIdentifierAllocator`

**Trạng thái: ĐÃ THỰC THI bước 1–5 ngày 2026-09-30, chưa commit.** Viết 2026-09-30 trên `develop`.
Mục 4 (Infrastructure) còn chờ.

**Phạm vi:** `Basil.Domain`, `Basil.Application`. **Ngoài phạm vi:** Infrastructure, `Basil.Host.*`,
test project (đang không build được do migration, xem `AGENTS.md`). Thay đổi schema tương ứng
được ghi ở mục 4 để làm sau.

**Kiểm tra sau mỗi bước:**

```bash
dotnet build src/Basil.Domain/Basil.Domain.csproj
dotnet build src/Basil.Application/Basil.Application.csproj
```

Cả hai phải xanh, không có warning mới so với trước bước.

---

## 0. Nguyên tắc đã chốt

- Việc sinh id bền vững (database id) là việc của nơi lưu trữ, không phải của Domain/Application.
  Application gửi **dữ liệu chưa có id**; lưu xong nhận lại **model hoàn chỉnh**.
- Model có id là **wrapper**: giữ định danh và `Value` là dữ liệu. Không dùng kế thừa.
  `IWrapper<T>` yêu cầu `T Value { get; }`.
- Truy cập dữ liệu trực tiếp qua `.Value` (`match.Value.Name`). Wrapper **không** forward từng
  thuộc tính. Runtime model vẫn forward như cũ: `Room.Name => Match.Value.Name`.
- Equality của wrapper theo id, như hiện tại.
- **Round không có database id.** Định danh của round là `(Match, Number)`, `Number` bắt đầu từ 1
  trong mỗi match. `Room` tự đếm (`LastRound.Number + 1`) dưới room scope, không chờ database.
  Round bị abort vẫn là một round (`Aborted = true`) và vẫn chiếm một số.
- **Bỏ `currentRoundId`.** Score lưu tham chiếu tới round mà nó được chơi trong đó (nếu có).
- **`Room.Id` do `Lobby` quản lý hoàn toàn**, là id định danh trong protocol (ghi dạng `ushort`).
  Không allocator.
- Sau kế hoạch này `IIdentifierAllocator<T>` không còn ai dùng và bị xoá.

## 1. Hình dạng đích

### 1.1 Domain

```csharp
// Basil.Domain/Utilities/IWrapper.cs
/// <summary>A model that wraps data with an identity.</summary>
public interface IWrapper<out T> { T Value { get; } }

// Basil.Domain/Multiplayer
public sealed class MatchData                  // Name (validate không rỗng), Creator, CreatedAt,
{ ... }                                        // EndedAt, IsVisible — chuyển nguyên từ Match
public sealed class Match : IWrapper<MatchData>, IEquatable<Match>
{
    public required int Id { get; init; }      // > 0
    public required MatchData Value { get; init; }
    // Equals/GetHashCode theo Id (giữ như hiện tại)
}

public sealed class Round : IMatchRecord, IEquatable<Round>
{
    public required Match Match { get; init; }
    public required int Number { get; init; } // > 0, thay cho Id
    // BeatmapHash, Settings, OccurredAt, EndedAt, Aborted giữ nguyên
    // Equals/GetHashCode theo (Match, Number)
}

// Basil.Domain/Users
public sealed partial class UserData { ... }   // Name (validate), Country, Privilege, SilenceEnd,
                                               // DeletedAt, regex — chuyển nguyên từ User
public sealed class User : IWrapper<UserData>, IEquatable<User>
{
    public required int Id { get; init; }
    public required UserData Value { get; init; }
    // Equals/GetHashCode theo Id
}

// Basil.Domain/Scores
public sealed record ScoreData(...)            // = Score hiện tại, thêm `Round? Round`
public sealed class Score : IWrapper<ScoreData>, IEquatable<Score>
{
    public required int Id { get; init; }      // > 0
    public required ScoreData Value { get; init; }
}
```

`Submission.Score` đổi kiểu sang `ScoreData`.

### 1.2 Application — port tạo mới

```csharp
// Basil.Application/Common/Persistence/ICreatable.cs
/// <summary>Creates items whose identity is assigned by the store.</summary>
public interface ICreatable<in TData, TValue>
    where TData : notnull where TValue : IWrapper<TData>
{
    /// <summary>Durably stores a new item and returns it with its assigned identity.</summary>
    Task<TValue> AddAsync(TData data, CancellationToken cancellationToken = default);
}
```

Remark của `IRepository.SaveAsync`: chỉ ghi item đã có định danh (không tạo item mới có id do nơi
lưu cấp).

## 2. Các bước

Mỗi bước là một commit, build xanh sau mỗi bước.

### Bước 1 — `refactor(domain)`: `IWrapper<T>`, `MatchData`/`Match`, `UserData`/`User`

- Thêm `IWrapper<T>`.
- Tách `Match` → `MatchData` + wrapper `Match`. Tách `User` → `UserData` + wrapper `User`.
  XML doc chuyển theo thuộc tính; tuân rule 6 của `AGENTS.md`.
- Sửa mọi nơi dùng trong Domain và Application: `x.Name` → `x.Value.Name`, v.v. (khoảng 15 chỗ
  cho `Match`, khoảng 10 chỗ cho `User`, gồm `Credentials`, `Login`, `MatchEvent`, `Beatmapset`,
  `Relationship`). `Room` giữ forwarding property, chỉ đổi đích sang `Match.Value.*`.
- Kiểm tra: build xanh; `grep -rn "IIdentifierAllocator" src/Basil.Application` vẫn còn (xoá ở
  bước 3).

### Bước 2 — `refactor(app)`: tạo Match qua `ICreatable`, Room.Id do Lobby cấp

- Thêm `ICreatable<TData, TValue>`; sửa remark `IRepository.SaveAsync`.
- `Lobby.CreateRoomAsync`:
  - `var match = await matches.AddAsync(new MatchData { Name, Creator, CreatedAt = UtcNow, EndedAt = null })`.
  - Room id: vòng lặp — chọn id nhỏ nhất trong `1..ushort.MaxValue` chưa có trong
    `rooms.AllById`, dựng `Room`, gọi `rooms.TryAdd`; nếu `false` (lần tạo đồng thời lấy mất id)
    thì chọn lại. Không còn id trống thì ném `InvalidOperationException`. **Không** thêm lock ở
    Lobby: `IRoomRegistry` đã thread-safe và `TryAdd` đã báo trùng; thêm lock là cơ chế đồng bộ
    thứ hai cho cùng state (cấm trong `AGENTS.md`).
  - Bỏ tham số `IIdentifierAllocator<Match>` và `IIdentifierAllocator<Room>`.
- Kiểm tra: build xanh.

### Bước 3 — `refactor(app)`: Round đánh số theo match

- `Room`:
  - `CurrentRound { get; private set; }` → `LastRound { get; private set; }` (không bao giờ bị
    xoá về null) và `CurrentRound => LastRound is { EndedAt: null } round ? round : null`.
    `InProgress` giữ nguyên nghĩa.
  - `Start(int roundId)` → `Start()`: `Number = (LastRound?.Number ?? 0) + 1`.
  - `Abort()`: bỏ `CurrentRound = null`; vẫn set `EndedAt`, `Aborted = true`.
- `MatchFlowCommands.StartAsync`, `RoomCountdowns.StartMatchAsync`: gọi `room.Start()`, bỏ
  `IIdentifierAllocator<Round>`. `MatchFlowCommands.StartAsync` có thể không còn cần `async`.
- `RoundEventHandlers`: giữ `SaveAsync` ở cả `RoundStarted`/`RoundAborted`/`RoundCompleted`; kiểu
  port đổi sang `IRepository<(int MatchId, int Number), Round>`.
- Xoá `Common/Persistence/IIdentifierAllocator.cs`.
- Kiểm tra: build xanh; `grep -rn "IIdentifierAllocator\|roundIdentifiers\|roomIdentifiers\|matchIdentifiers" src/Basil.Domain src/Basil.Application`
  trả về rỗng.

### Bước 4 — `refactor(domain,app)`: Score là wrapper, gắn Round

- Tách `Score` → `ScoreData` (thêm `Round? Round`) + wrapper `Score`. `Submission.Score` đổi sang
  `ScoreData`.
- `ScoreSubmission.SubmitAsync`:
  - Bỏ tham số `scoreId`.
  - Port `IRepository<int, Score>` → `ICreatable<ScoreData, Score>`.
  - Round được gắn: `session.Room?.LastRound`, **chỉ khi** `round.BeatmapHash == score.BeatmapHash`;
    ngược lại `null`.
  - `var score = await scores.AddAsync(submission.Score with { UserId = session.User.Id, Round = round }, ct);`
  - Replay lưu dưới `score.Id`, nhờ vậy key replay luôn khớp id score.
  - Vì `LastRound` không bị xoá khi round kết thúc, score tới sau packet kết thúc round vẫn gắn
    đúng round. Điều kiện beatmap chặn trường hợp round kế tiếp (map khác) đã bắt đầu trước khi
    score tới. Giới hạn đã biết: hai round liên tiếp cùng map thì score tới muộn vẫn gắn vào round
    mới hơn.
- Kiểm tra: build xanh.

### Bước 5 — `docs`: cập nhật tài liệu

- `docs/for-developers/database.md`, mục "Scores must attach to the correct round": thay cơ chế
  `currentRoundId` bằng "score giữ tham chiếu round; round định danh bằng `(match, number)`;
  round bị abort vẫn chiếm một số".
- `docs/for-developers/multiplayer.md`: phần round id và room id (room id là id protocol do
  lobby cấp, tối đa `ushort`).
- Kiểm tra: `grep -rn "currentRoundId" docs` chỉ còn trong ngữ cảnh lịch sử (nếu có).

## 3. Rủi ro

- Bước 1 chạm nhiều file nhất (đổi `.Name` → `.Value.Name`). Là thay đổi cơ học; compiler bắt
  hết chỗ sót.
- `Room.LastRound` mở rộng vòng đời object `Round` tới round kế tiếp; không có hệ quả bộ nhớ đáng kể.
- Round được đánh số ở runtime: vì room mất khi restart và mỗi room luôn tạo match mới, không có
  trường hợp hai room cùng đánh số một match. Nếu sau này có tính năng mở lại match cũ, `Room`
  phải khởi tạo `LastRound` từ round cuối đã lưu.
- Match được insert trước khi chọn room id: khi pool room id đầy (65.535 phòng mở cùng lúc) sẽ
  còn một match mồ côi. Chấp nhận được vì gần như không xảy ra.

## 4. Việc tiếp theo ngoài phạm vi (Infrastructure)

- `Matches`: bỏ cột `currentRoundId`.
- `Rounds`: khoá chính `(MatchId, Number)` thay cho `Id` AUTOINCREMENT.
- `Scores`: thêm `(MatchId, RoundNumber)` nullable tham chiếu `Rounds`.
- Repository Match và Score implement `ICreatable` (INSERT … RETURNING Id).
- Mọi chỗ dựng và đọc `User`/`Match` ở Infrastructure và Host phải đổi sang
  `new User { Id, Value = new UserData { … } }` và `.Value.X` (ví dụ `SqliteUserRepository`,
  `SqliteMatchRepository`, `UserRoutes.SampleUser`).
- Khi Application có luồng đăng ký: `ICreatable<UserData, User>`, rồi `credentials.SaveAsync`.
