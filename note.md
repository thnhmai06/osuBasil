# Ghi chú review — Phase 6 (score submission) hoàn thành, phiên làm việc tự động

Bạn dặn tôi cứ tiếp tục qua các phase mà không hỏi lại vì đi ngủ. Phase 6 đã xong hoàn toàn (12/12 task), build sạch, toàn bộ test xanh. Dưới đây là log quyết định/đánh đổi cần bạn xem lại.

## Kết quả

- **Tất cả test xanh toàn solution**: Domain 136, Protocol 140, Application 251, Infrastructure 98, Architecture 9, Integration 32.
- Plan file (`glowing-snacking-hickey.md`) đã cập nhật phần Phase 6 đầy đủ, theo đúng văn phong các phase trước.
- **Chưa commit** — để bạn xem note này trước, git status hiện có toàn bộ thay đổi Phase 6 nằm trong working tree.

## Phát hiện quan trọng nhất

Score submission của bancho.py gate CẢ việc tính status/placement (thuần DB, không cần file) LẪN việc tính pp/sr (cần đọc file `.osu`) đằng sau CÙNG một điều kiện `if osu_file_available`. Đây là tình cờ của control flow Python, không phải phụ thuộc thật. bancho-net bỏ hẳn gate này — status/placement luôn chạy, không cần fetch gì cả. Xác nhận lại "100% offline" hoạt động được cho phase khó nhất.

## Quyết định/đánh đổi tự đưa ra (không hỏi lại — xin xem kỹ, dễ sửa nếu bạn không đồng ý)

1. **`Beatmap.HasLeaderboardStrict`** (Ranked/Approved/Loved, loại Qualified) và **`Beatmap.AwardsRankedScore`** (chỉ Ranked/Approved) — hai predicate MỚI, tách biệt với `HasLeaderboard` cũ (dùng cho getscores). Nếu lỡ dùng nhầm `HasLeaderboard` ở chỗ cần 2 cái mới này sẽ vô tình cho map Qualified/Loved được tính ranked score — đã kiểm tra kỹ, không nhầm.
2. **Checksum test dùng oracle chép tay bằng Python thuần** (không gọi hàm thật `compute_online_checksum` vì thiếu `fastapi` trong môi trường) — chỉ bắt lỗi transcribe, chưa xác nhận đúng giao thức thật 100%. Ngược lại **oracle Rijndael decryptor ĐÃ dùng hàm encrypt thật** (cài `py3rijndael` tạm qua pip) — đáng tin hơn.
3. ~~Bỏ cơ chế snapshot/restore của Python...~~ **ĐÃ FIX (2026-07-07, theo yêu cầu)**: thêm `IScoreSubmissionPersistence`/`MySqlScoreSubmissionPersistence` — mở 1 connection, 1 transaction, thực hiện demote-previous-best + insert-score + update-stats trong CÙNG transaction đó rồi mới commit, khớp với Python's `async with self.database.transaction():`. `ScoreSubmissionUseCase` giờ gọi method atomic này thay vì 3 lệnh riêng qua `IScoreRepository`/`IStatsRepository`. Có test Testcontainers xác nhận THẬT rollback hoạt động (ép lỗi bằng cách chèn `grade` dài hơn `varchar(2)`, xác nhận demotion của điểm cũ bị rollback lại thành BEST thay vì kẹt ở SUBMITTED).
4. **Restrict-khi-thiếu-replay**: giữ kiểm tra hợp lệ (≥24 byte) + discard replay xấu, KHÔNG trừng phạt thật (chưa có hệ thống restriction) — đánh dấu `TODO(restriction-phase)` trong code. Điểm vẫn tính bình thường.
5. **Bỏ hẳn (deferred) khỏi core path vì không ảnh hưởng response cho client**: publish-user-stats broadcast, personal-best/first-place chat announce. Achievements đã bỏ từ đầu dự án (không phải quyết định riêng phase này).
6. **`validate_submission_integrity` thất bại không chặn submission** — y hệt Python hiện tại (đang "trial period", restrict bị comment out trong source gốc).
7. Thêm `ClientDetails` vào `PlayerSession` (chưa có từ Phase 3) — cần cho việc so khớp client_hash lúc submit.
8. Thêm `StorageOptions.ReplaysPath` (config mới, default `.data/osr`).
9. Format số trong charts response chưa khớp 100% cách Python format float (cosmetic, chưa rõ ảnh hưởng client thật).
10. `ScoreSubmissionUseCase`'s `ChecksumLocks` (`ConcurrentDictionary<string, SemaphoreSlim>`) không bao giờ evict — mỗi checksum duy nhất giữ 1 semaphore mãi mãi, unbounded growth. Nhỏ, không chặn, nhưng cần nhớ dọn sau (việc evict an toàn không tầm thường vì có thể race với request đang chờ lock).

## Còn lại cho bạn (Phase 6)

Toàn bộ luồng submit CHƯA test với client osu! thật (cần replay hợp lệ + server local — xem kinh nghiệm `bancho_net_local_client_testing`). Phase 6 đã commit (`22a5f2f` + `473ee70` sửa lại note này) — không có gì đang treo lửng trong working tree.

---

# Phase 7 (Spectator + Multiplayer + tourney) — MỚI BẮT ĐẦU NGHIÊN CỨU, CHƯA VIẾT CODE

Sau khi commit Phase 6, tôi hỏi advisor trước khi lao vào Phase 7 (đây là phase "trọng tâm chính" của cả dự án theo plan file). Advisor cảnh báo rõ: cách làm ở Phase 3-6 là "dịch trung thực từ source tuần tự đã đúng" — cách đó sẽ ĐÁNH LỪA tôi ở Phase 7, vì Python's `match.py` dựa vào tính atomic của asyncio event loop giữa các `await` (KHÔNG có lock nào cả), còn ASP.NET Core chạy thread-pool thật — copy y nguyên logic sẽ tạo ra race condition thật. Advisor dặn: **viết race test cho thiết kế đồng bộ hóa TRƯỚC khi port 22 match packet**, và **chia Phase 7 thành nhiều commit nhỏ** (spectator trước — rủi ro thấp — rồi mới đến MatchSession core + race test — rồi 22 packet — rồi `!mp`/`!pool` — rồi HTML pages), KHÔNG gộp thành 1 commit khổng lồ như Phase 6.

Tôi đã dừng lại sau khi nghiên cứu xong phần Spectator (thấy rủi ro thấp, độc lập với Multiplayer) nhưng CHƯA VIẾT CODE, vì phát hiện một refactor cần thiết lớn hơn dự kiến (xem bên dưới) — dừng ở đây để không để lại code dở dang chưa test trong working tree.

## Đã xác nhận (nghiên cứu xong, đọc source Python + code bancho-net hiện tại)

1. **`app/objects/match.py` KHÔNG có lock nào** — xác nhận đúng như advisor nghi ngờ. Nghĩa là Phase 7 phải TỰ THIẾT KẾ cơ chế đồng bộ cho `MatchSession` (actor-per-match qua channel hoặc lock tường minh), không có gì để "port" — chính plan file cũng đã dự tính điều này ("actor-per-match, test race bắt buộc").
2. **Protocol layer (`Bancho.Protocol`) ĐÃ CÓ SẴN toàn bộ opcode + packet writer cho spectator** (từ Phase 0/1, đi trước nhu cầu): `ClientPackets.StartSpectating/StopSpectating/SpectateFrames/CantSpectate`, `ServerPackets.SpectatorJoined/SpectatorLeft/SpectateFrames/SpectatorCantSpectate/FellowSpectatorJoined/FellowSpectatorLeft`, và các hàm tương ứng trong `ServerPacketWriter`. Phần này KHÔNG cần làm gì thêm ở Protocol layer.
3. **Phát hiện quan trọng cần refactor trước khi làm spectator**: Python's `Channel` có `real_name` (tên thật, dùng làm key nội bộ, vd `#spec_123`) TÁCH BIỆT với `name` (tên hiển thị cho client, luôn là `#spectator` cho mọi kênh bắt đầu bằng `#spec_`, `#multiplayer` cho `#multi_`). Lý do: nhiều phiên spectate/multiplayer chạy đồng thời, nhưng client luôn gọi kênh hiện tại của nó là literally `"#spectator"`/`"#multiplayer"` bất kể đang xem ai. **`Bancho.Application.Sessions.ChannelSession` hiện tại của bancho-net chỉ có MỘT field `Name`** dùng vừa làm registry key vừa làm tên gửi client — CHƯA đủ cho instance channel. Cần thêm field `DisplayName` (hoặc tương đương) và cập nhật mọi chỗ gửi `ChannelInfo`/`SendMessage` dùng `DisplayName` thay vì `Name` khi là kênh instance.
4. **`IChannelRegistry` hiện tại (`Seed`/`GetByName`/`AutoJoinChannels`/`All`) chỉ hỗ trợ kênh tĩnh nạp từ DB lúc khởi động** — KHÔNG có method để thêm/xoá kênh động lúc runtime (`#spec_{hostId}` tạo khi bắt đầu spectate, xoá khi spectator cuối rời đi — `instance=True` bên Python). Cần thêm `Add(ChannelSession)`/`Remove(name)` vào interface — ảnh hưởng tới `InMemoryChannelRegistry` (Infrastructure) và có thể cả nơi khác dùng interface này (`ChannelJoinHandler`, `ChannelPartHandler`, `SendPublicMessageHandler`, `CommandDispatcher`, `OsuLoginUseCase`, `PlayerLogoutService` — 7 file tham chiếu `ChannelSession`/`IChannelRegistry`, cần rà lại từng chỗ trước khi đổi field `Name`).
5. Cần thêm field mới cho `PlayerSession`: `Spectating` (PlayerSession? — đang xem ai), `Spectators` (tập hợp người đang xem mình), `Stealth` (bool, mặc định false — chế độ xem lén cho admin, lệnh `!stealth` toggle nó nhưng lệnh đó thuộc Phase 10 admin commands, không phải Phase 7 — chỉ cần field ở đây, chưa cần lệnh toggle).

## Việc tiếp theo (thứ tự đề xuất, theo đúng lời dặn của advisor — từng commit riêng)

1. Refactor `ChannelSession`/`IChannelRegistry` cho instance channel (mục 3+4 trên) — kèm test, chạy lại toàn bộ Application+Infrastructure test trước khi commit riêng phần này (không gộp với spectator).
2. Spectator: 4 packet handler (`StartSpectatingHandler`, `StopSpectatingHandler`, `SpectateFramesHandler`, `CantSpectateHandler`) + field mới trên `PlayerSession` — commit riêng.
3. `MatchSession` core: **viết race test trước** (N thread đổi slot đồng thời) để chọn cơ chế đồng bộ đúng, RỒI mới port 22 match packet lên trên nền đó — đây là phần rủi ro cao nhất, đừng vội.
4. `!mp *` (25 lệnh)/`!pool *` — sau khi MatchSession ổn định.
5. `GET /matches`, `/online` HTML pages — cuối cùng, ít rủi ro nhất.

Nếu bạn thức dậy và tôi vẫn đang chạy, tôi sẽ tiếp tục theo đúng thứ tự trên. Nếu phiên đã dừng, đây là điểm bắt đầu chính xác cho lần tiếp theo — không cần đọc lại source Python từ đầu, mọi phát hiện đã ghi ở trên.

## CẬP NHẬT: mục 1+2 (refactor Channel + Spectator) đã XONG, đã commit

Sau khi ghi note trên, tôi tiếp tục làm luôn mục 1 (refactor `ChannelSession`/`IChannelRegistry`) và mục 2 (spectator) trong CÙNG session — quyết định gộp 2 mục này vì refactor Channel chỉ có ý nghĩa khi có spectator dùng tới, tách riêng sẽ để lại code chết tạm thời. Đã KHÔNG động vào `ChannelJoinHandler`/`ChannelPartHandler` cũ (giữ nguyên hành vi, tránh rủi ro ngoài phạm vi).

**Đã làm**: `ChannelSession` thêm `DisplayName`/`Instance`/`MemberIds` (additive, không phá call site cũ nhờ default param); `IChannelRegistry` thêm `Add`/`Remove`; `ChannelMembershipService` mới (join/part dùng chung, broadcast scope khác nhau giữa kênh instance và kênh thường); `SpectatorService` mới (port `add_spectator`/`remove_spectator`, đơn giản hoá 1 chỗ: gộp 2 lần gửi channel_info trùng lặp của Python thành 1 lần — không mất thông tin, chỉ bớt gói tin dư); 4 packet handler mới (`StartSpectatingHandler`/`StopSpectatingHandler`/`SpectateFramesHandler`/`CantSpectateHandler`); `PlayerSession` thêm `Spectating`/`Spectators`/`Stealth`; `PlayerLogoutService` thêm dọn dẹp "đang xem ai" khi logout (đúng theo `Player.logout`, KHÔNG dọn "ai đang xem mình" vì Python cũng không làm — có thể là gap có sẵn trong bancho.py, không phải tôi bỏ sót).

**Test**: toàn bộ solution xanh — Domain 136, Protocol 140, Application 275, Infrastructure 104, Architecture 9, Integration 32.

**Đã commit riêng** (không gộp với phần MatchSession sau này — xem lịch sử git để biết hash chính xác).

## CẬP NHẬT (2026-07-07): fix transaction Phase 6 đã commit (`7b5a2f9`), đổi thiết kế đồng bộ Phase 7

Theo yêu cầu, đã bọc transaction thật cho score submission persistence (mục 3 ở trên, giờ đã ĐÃ FIX — xem dòng đó). Commit riêng, không gộp với Phase 7.

**Hỏi lại advisor trước khi viết `MatchSession`**: advisor phản bác ý tưởng "actor-per-match" ghi trong plan file trước đó — cho rằng quá nặng máy (background Task/message type/result correlation) so với kiến trúc handler đồng bộ hiện có. Câu hỏi phân định: có handler nào cần giữ exclusivity qua một `await` không? KHÔNG — toàn bộ handler quản lý slot (create/join/part/change-slot/ready/lock/settings/mods/team/transfer/start/no-map/has-map/not-ready/failed/skip) là mutate đồng bộ + broadcast. Nên **đổi sang 1 `SemaphoreSlim(1,1)` per-match** (property `MatchSession.Lock`), giữ trong lock suốt "đọc state → mutate slot → serialize update_match → enqueue broadcast → release". Ngoại lệ duy nhất `await_submissions` (poll 10s) thuộc slice scoring sau, không giữ lock qua await đó — không ảnh hưởng slice hiện tại. Đã cập nhật plan file phản ánh đổi ý này (trước đó ghi actor-per-match).

**Đã làm xong (commit 2/5 theo thứ tự advisor đề ra: race test → registry → state model)**:
- `Bancho.Application.Sessions.MatchSlot` — port `Slot` (PlayerId/Status/Team/Mods/Loaded/Skipped + Empty/CopyFrom/Reset).
- `Bancho.Application.Sessions.MatchSession` — port `Match` (không gồm scrim/tourney-scoring/timer fields — để dành slice sau khi cần), có sẵn `Lock` (SemaphoreSlim), `GetSlot`/`GetSlotId`/`GetFreeSlotId`/`GetHostSlot`/`UnreadyPlayers`/`ResetPlayersLoadedStatus`, referee set (`IsReferee` = host hoặc trong `_referees`, port `Match.refs`), tourney-client set. `SlotStatus`/`MatchTeams`/`MatchWinConditions`/`MatchTeamTypes` đã có sẵn trong Domain từ trước (scaffold đi trước nhu cầu, giống Protocol layer) — không cần tạo lại.
- `IMatchRegistry`/`InMemoryMatchRegistry` — port `Matches` collection (bảng cố định 64 slot, `TryCreate` tìm slot trống + đăng ký atomic dưới 1 lock cấp registry — khác với lock cấp match của `MatchSession.Lock`, đây là 2 tầng lock riêng biệt: registry lock bảo vệ việc cấp phát id, match lock bảo vệ mutate slot của 1 match cụ thể).
- **Race test** (`MatchSessionRaceTests.cs`) — 2 test chứng minh cả hai chiều: (1) `UnsynchronizedFreeSlotLookup_CanLoseAPlayerToADoubleAssignment` tái tạo đúng race `get_free()+occupy` không atomic bằng cách chèn `Task.Delay` giữa đọc và ghi KHÔNG qua lock, xác nhận race THẬT xảy ra (1 player bị ghi đè mất); (2) `ConcurrentJoins_UnderLock_*` 16-20 thread join đồng thời qua `match.Lock`, xác nhận không bao giờ trùng slot và đúng số lượng join thành công/từ chối. Chạy lại 3 lần liên tiếp không flaky.
- DI: đăng ký `IMatchRegistry` singleton, thêm test `CompositionRootTests.ResolvesMatchRegistryAsASharedSingleton`.
- Test: Application 291 (+16), Infrastructure +9 (registry) — toàn bộ xanh, `dotnet build` cả solution sạch, `ArchitectureTests` 9/9 xanh (không vi phạm ranh giới layer).

Đã commit 2/5 (`4131fce`) — 116/116 test Infrastructure xanh trước khi commit, không regression.

## CẬP NHẬT: commit 3/5 (18 packet quản lý slot) đã XONG

Port toàn bộ 18 packet quản lý slot lên nền `MatchSession.Lock` (không đụng scoring): CREATE_MATCH, JOIN_MATCH, PART_MATCH, MATCH_CHANGE_SLOT, MATCH_READY, MATCH_LOCK, MATCH_CHANGE_SETTINGS, MATCH_START, MATCH_CHANGE_MODS, MATCH_LOAD_COMPLETE, MATCH_NO_BEATMAP, MATCH_NOT_READY, MATCH_FAILED, MATCH_HAS_BEATMAP, MATCH_SKIP_REQUEST, MATCH_TRANSFER_HOST, MATCH_CHANGE_TEAM, MATCH_CHANGE_PASSWORD, MATCH_SCORE_UPDATE (19 packet thật ra, đếm lại — MATCH_SCORE_UPDATE chỉ forward raw byte, không phụ thuộc scoring nên gộp vào đây luôn).

**Kiến trúc mới**:
- `MatchMembershipService` (Application.UseCases.Multiplayer) — port `Player.join_match`/`leave_match` + `Match.enqueue`/`enqueue_state`. Mọi method đọc-rồi-sửa slot/settings giả định caller ĐÃ giữ `match.Lock` (handler sở hữu vòng đời lock, vì lock phải bao trùm luôn bước broadcast — đúng theo lời dặn advisor). `Create` tự cấp phát id qua `IMatchRegistry.TryCreate` + tạo kênh `#multi_{id}` (tái dùng `ChannelSession`/`ChannelMembershipService`/`IChannelRegistry.Add` đã có từ spectator) + join host vào slot 0.
- `ChannelMembershipService` thêm `BroadcastToMembers` (port `Channel.enqueue` — gửi packet thô cho member hiện tại của kênh, có immune list) — dùng chung cho match, không chỉ riêng ChannelInfo như trước.
- `MatchPacketDataMapper` — map `MatchSession` sang `MatchPacketData` (Protocol DTO) cho `ServerPacketWriter.UpdateMatch`/`MatchJoinSuccess`/`MatchStart`.
- `PlayerSession` thêm field `Match` (port `Player.match`).
- `Mods.cs` thêm `ModsExtensions.SpeedChangingMods` (DT|NC|HT, port `SPEED_CHANGING_MODS`).
- `MatchMembershipService.ValidateMatchData` (static, port `validate_match_data`) dùng chung cho CreateMatch/MatchChangeSettings/MatchChangePassword — 3 chỗ Python gọi cùng 1 hàm.

**Quyết định/đánh đổi tự đưa ra**:
1. `MatchChangeSettingsHandler` (async, cần `IMapRepository.FetchOneAsync` để tra map theo md5) giữ `match.Lock` xuyên qua await đó — khác nguyên tắc chung "không giữ lock qua await" mà advisor dặn, NHƯNG advisor chỉ áp dụng ngoại lệ đó cho `await_submissions` (poll 10s), còn đây là 1 lượt tra DB thường (repository call, không phải poll dài) — đã cân nhắc kỹ, chấp nhận được vì tránh phải làm release-fetch-reacquire-recheck phức tạp cho thao tác hiếm gặp (đổi map) và không rủi ro deadlock (SemaphoreSlim hỗ trợ đầy đủ async wait/release). Nếu về sau đo được nghẽn thật ở đây thì mới nên tách.
2. `Player.update_latest_activity_soon()` — KHÔNG port (chưa có cơ chế debounced-DB-write nào trong bancho-net); bỏ hẳn khỏi mọi handler match vì đây là optimization ghi DB không ảnh hưởng hành vi client thấy được.
3. Nhánh `is_scrimming` trong MATCH_CHANGE_SETTINGS (gửi tin nhắn bot thay vì đổi team type khi đang scrim) KHÔNG viết — vì `MatchSession.IsScrimming` luôn `false` ở slice này (field scrim chưa port), nên nhánh đó không bao giờ chạy được; code chỉ viết nhánh else (luôn đúng cho mọi match hiện tại). Sẽ bổ sung khi tới slice scrim.
4. `MATCH_LOCK`/`MATCH_TRANSFER_HOST` chỉ kiểm tra "player is match host" (đúng Python — không dùng `IsReferee`/`Match.refs` như tôi tưởng ban đầu khi đọc code; refs chỉ dùng cho `!mp` commands sau này).

**Test**: `MatchMembershipServiceTests` (13 test, thư mục `UseCases/Multiplayer`) + 1 file test/handler (19 file, `PacketHandlers/`, dùng chung fixture `MultiplayerTestSupport.cs` để tránh lặp fake registry ở mọi file) — tổng 56 test mới, toàn bộ xanh lần chạy đầu. Application.Tests 347 tổng, ArchitectureTests 9/9, `CompositionRootTests` cập nhật số handler kỳ vọng 20→39, xanh với DI thật.

**CHƯA COMMIT tại thời điểm ghi note này** — đang chờ full `Infrastructure.Tests` chạy nền lần 2 làm baseline trước khi commit 3/5.

## Việc CÒN LẠI cho Phase 7

Sau commit 3/5: **cố ý HOÃN sang commit sau** (không phải bỏ) — MATCH_COMPLETE (cần tách phần slot-management ra khỏi phần `update_matchpoints`/scrim scoring + field `recent_score` trên PlayerSession cho `await_submissions`), MATCH_INVITE, 3 packet tourney (`TOURNAMENT_MATCH_INFO_REQUEST`/`JOIN_MATCH_CHANNEL`/`LEAVE_MATCH_CHANNEL`), rồi `!mp`/`!pool` (25 lệnh), rồi HTML pages `/matches`/`/online`. `MatchSession` cố ý CHƯA có field scrim (`match_points`/`bans`/`winners`/`winning_pts`/`use_pp_scoring`) và timer (`starting`) — thêm khi tới slice cần.
