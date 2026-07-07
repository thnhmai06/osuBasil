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

## CẬP NHẬT: commit 4/5 (MATCH_COMPLETE + MATCH_INVITE + 3 packet tourney + fix logout) đã XONG

Hỏi lại advisor sau commit 3/5 trước khi tiếp tục — 2 phát hiện quan trọng đã sửa ngay trong commit này:

1. **MATCH_COMPLETE KHÔNG phụ thuộc scoring như tôi tưởng ban đầu** — soi lại control flow: nhánh gọi `update_matchpoints`/`await_submissions` chỉ chạy `if self.match.is_scrimming`, mà `IsScrimming` luôn `false` ở slice này (scrim chưa port) → nhánh đó chết, phần còn lại (đánh dấu slot Complete, kiểm tra còn ai đang Playing không, `unready_players`/`reset_players_loaded_status`, `in_progress=false`, broadcast `match_complete` immune not-playing + `enqueue_state`) chỉ dùng method `MatchSession` đã có sẵn — port luôn, không hoãn nữa. `MatchCompleteHandler` mới.
2. **Lỗ hổng thật đã sửa**: `PlayerLogoutService` trước đó có comment "match cleanup lands later" — nghĩa là user thật disconnect giữa trận sẽ để lại slot chiếm chỗ MÃI MÃI (match không bao giờ rỗng → không bao giờ bị dispose, nếu là host thì phòng kẹt luôn không ai điều khiển được). Test unit không thấy được lỗi này (logout là service riêng, không liên quan slot-management), nhưng sẽ lộ ngay khi test với client thật ở cổng phase. Đã thêm: nếu `player.Match` khác null, giữ `match.Lock`, gọi `MatchMembershipService.Leave`, release — thêm 1 test `Logout_WhileInAMatch_LeavesTheMatchSoItDoesNotAccumulateAGhostSlot` xác nhận match bị dispose đúng khi người cuối cùng logout.

**Cũng port**: `MatchInviteHandler` (thêm `MatchSession.Url`/`Embed` port `Match.url`/`Match.embed`, xử lý riêng case target là bot → gửi tin nhắn "I'm too busy!" thay vì mời), 3 packet tourney (`TourneyMatchInfoRequestHandler`/`TourneyMatchJoinChannelHandler`/`TourneyMatchLeaveChannelHandler` — donator-only, join/leave kênh chat của match mà KHÔNG chiếm slot, dùng trực tiếp `ChannelMembershipService.Join`/`Part` trên kênh `#multi_{id}`, không qua `MatchMembershipService.Join` vì đó là dành cho người chơi thật).

**Test**: 30 test mới (`MatchCompleteHandlerTests`, `MatchInviteHandlerTests`, 3 file tourney, `PlayerLogoutServiceTests` +1) — xanh lần chạy đầu. Application.Tests 359 tổng, Infrastructure 116, Architecture 9/9, CompositionRoot cập nhật 39→44 handler.

**⚠️ CẦN BẠN QUYẾT ĐỊNH (advisor chủ động flag, không tự quyết luôn)**: **Scrim (`!mp scrim`, match points, xác định người thắng qua `update_matchpoints`/`await_submissions`, ban mod) là tính năng khá lớn, CHỈ dùng cho giải đấu (tourney).** `MatchSession` hiện CHƯA có field nào cho scrim (`match_points`/`bans`/`winners`/`winning_pts`/`use_pp_scoring`) — tôi đang mặc định HOÃN (không xoá hẳn, có thể thêm sau) theo đúng tinh thần các quyết định thu hẹp phạm vi trước đó của bạn (bỏ pp, bỏ achievements). Nếu bạn CẦN scrim cho server của mình (vd sẽ tổ chức giải đấu), báo lại để tôi làm slice riêng; nếu không cần, tôi sẽ chính thức đánh dấu "bỏ" trong plan file thay vì "hoãn".

## CẬP NHẬT: bạn trả lời câu hỏi scrim — "Cần ngay bây giờ". Đã làm xong ENGINE scrim (commit riêng, sau 4/5)

Bạn chọn xây scrim ngay (không hoãn, không bỏ). Hỏi lại advisor để thiết kế đúng phần khó nhất: launch background scoring task ở đâu cho không phá vỡ model lock đã xây từ trước.

**Phát hiện quan trọng của advisor (đã áp dụng, khác 1 chỗ so với dự định ban đầu của tôi)**: KHÔNG được khởi chạy task chấm điểm (`update_matchpoints`) TỪ BÊN TRONG lock của MATCH_COMPLETE — nếu làm vậy, việc đầu tiên task đó làm là `WaitAsync` xin lại đúng cái lock người gọi đang giữ (tự deadlock/chờ), và dù có "chạy được" thì cũng giữ lock suốt 10s poll, đóng băng mọi mutate slot khác của match đó — đúng lỗi treo phòng mà advisor cảnh báo trước đó. Cách đúng: dưới lock chỉ mutate state + chụp snapshot (`wasPlayingIds+team`, `mapMd5`, `winCondition`, `teamType`, `matchName`) + cờ `shouldScore`, RELEASE lock, RỒI mới fire-and-forget task ở ngoài. Bên trong task: poll KHÔNG giữ lock nào (đọc `PlayerSession.RecentScore`, không đụng match), chỉ giữ lock lại 1 lần ngắn ở cuối để ghi `match_points`/`winners`/gửi thông báo.

**Đã làm**:
- `PlayerSession.RecentScore` (field mới, kiểu `RecentScoreSnapshot{BeatmapMd5,ServerTime,Score,Acc,MaxCombo}`) — port `Player.recent_score`, ĐƠN GIẢN HOÁ từ dict theo-mode của Python (Python chọn bản ghi mới nhất theo `server_time` giữa các mode) thành 1 field duy nhất luôn ghi đè (đủ dùng, 1 người không chơi 2 mode cùng lúc trong 1 trận) — set ở cuối `ScoreSubmissionUseCase.SubmitAsync` (mở lại Phase 6, chỉ thêm đúng 1 dòng này, không đụng gì khác).
- `MatchSession` thêm field scrim: `MatchPoints`/`AddMatchPoint`/`GetMatchPoints` (port `match_points`), `Bans`/`AddBan` (port `bans`, tuple `(Mods, mapId)`), `Winners`/`RecordWinner` (port `winners`, `null` = hoà), `WinningPoints`, `ResetScrim` (port `reset_scrim` — xoá match_points/winners/bans, KHÔNG đụng `IsScrimming`).
- `ScrimParticipant` (struct mới, `Sessions/ScrimParticipant.cs`) — port union `MatchTeams | Player` mà Python dùng làm key cho `match_points`/`winners`, vì C# không có union type: struct chứa `Team?`/`PlayerId?`, đúng 1 trong 2 luôn có giá trị.
- `MatchScoringService` (mới, `UseCases/Multiplayer/`) — port `Match.await_submissions` + `Match.update_matchpoints`, cấu hình được `pollInterval`/`pollTimeout` (mặc định 500ms/10s, test dùng vài ms để không phải chờ thật) thay vì hard-code như Python, cho test được nhanh. Bỏ hẳn `use_pp_scoring` (theo quyết định no-pp toàn dự án) — win condition luôn theo `(score, acc, max_combo, score)[win_condition]`. **Khác Python 1 chỗ, AN TOÀN HƠN**: Python giữ tham chiếu `Slot` và `assert s.player is not None` giữa chừng poll — sẽ crash nếu người chơi rời trận trong lúc chờ (10s). Bản port chụp snapshot player ID lúc MATCH_COMPLETE (không giữ tham chiếu Slot), tra lại session lúc chấm điểm — người đã logout giữa chừng đơn giản bị tính "không nộp điểm" thay vì crash.
- `MatchMembershipService.SendBot` mới (port `Channel.send_bot` — CHỈ gửi cho member kênh chat của match, KHÔNG mirror sang lobby như `Enqueue`/`EnqueueState`). Nhân tiện port luôn dòng `match.chat.send_bot(f"Match created by {player.name}.")` mà tôi bỏ sót ở `CreateMatchHandler` (commit 3/5).
- `MatchCompleteHandler` cập nhật: dưới lock chụp `wasPlaying` (playerId+team, đúng thời điểm TRƯỚC khi `unready_players` chạy — khớp thứ tự Python), snapshot rồi release lock, sau đó nếu `match.IsScrimming` mới `_ = scoringService.ScoreCompletedRoundAsync(...)` (fire-and-forget, có try/catch bên trong gửi "Scores could not be calculated." nếu lỗi — task không await không được để exception biến mất im lặng).

**Test**: `MatchScoringServiceTests` (6 test: FFA thắng theo điểm cao hơn, hoà, teams có tên đội từ regex tên trận `OWC2020: (A) vs. (B)`, đạt đủ `WinningPoints` thì reset scrim, timeout không nộp điểm bị loại, không tìm thấy beatmap thì không crash) + 1 test tích hợp trong `MatchCompleteHandlerTests` (xác nhận handler thật sự launch task chấm điểm sau khi release lock, dùng poll budget vài ms để không phải chờ thật) + 6 test mới cho `MatchSession` scrim helpers + `ScrimParticipant`. Application.Tests 371 tổng, ArchitectureTests 9/9, `CompositionRootTests` xanh (không đổi số handler vì `MatchScoringService` không phải packet handler).

**Đã commit** `9c2dd3f` sau khi `Infrastructure.Tests` chạy nền bị kill 2 lần (không phải regression — Docker/Testcontainers vẫn khoẻ, `docker ps`/`docker info` bình thường, có vẻ tiến trình nền bị dừng ngoài ý muốn) rồi chạy lại foreground xác nhận 116/116 xanh.

## Fix nhỏ trước khi sang `!mp`: bug làm tròn accuracy trong scrim engine

Advisor phát hiện: bộ tích luỹ điểm mỗi round trong `MatchScoringService.AwaitSubmissionsAsync`/`UpdateMatchPointsAsync` khai kiểu `Dictionary<ScrimParticipant, long>`, và nhánh `MatchWinConditions.Accuracy` ép `(long)recent.Acc` — làm tròn accuracy về số nguyên phần trăm. Python annotate kiểu `int` cho dict này nhưng runtime thực chất cộng dồn `float` thô (`rc_score.acc`) — annotation nói dối. Hậu quả: nếu 1 scrim dùng win condition Accuracy, 97.6% vs 97.2% sẽ bị làm tròn thành 97 vs 97 → hoà giả trong khi thực ra có người thắng. Sửa: đổi toàn bộ `Dictionary<ScrimParticipant, long>` → `Dictionary<ScrimParticipant, double>` (`MatchScoringService.cs`), `AddSuffix` nhận `double` (ép `(long)` chỉ khi format hiển thị Score/Combo, giữ nguyên độ chính xác khi so sánh thắng/thua). Score/Combo không bị ảnh hưởng (vốn đã là số nguyên chính xác). Đã build sạch + chạy lại `MatchScoringServiceTests`/`MatchCompleteHandlerTests`/`MatchSessionTests` (27/27) + full Application.Tests (382/382) — không có regression.

## `!mp`/`!pool` commands — bắt đầu, đã tư vấn advisor về cách chia lát cắt

Đọc kỹ `app/commands.py`: `CommandSet` (trigger "mp"/"pool"/"clan", danh sách `Command` riêng), `process_commands` dispatch 2 tầng (tầng ngoài khớp "mp"/"pool" → mặc định `["help"]` nếu không có subcommand → tầng trong khớp subcommand trong `cmd_set.commands`, gate theo priv của TỪNG subcommand) khác hẳn `ICommand`/`CommandDispatcher` hiện có ở bancho-net (phẳng, 1 tầng, gate priv của chính command). `ensure_match` decorator bọc MỌI hàm `mp_*`: bắt buộc đang ở trong 1 trận, lệnh phải gửi ĐÚNG kênh chat của trận đó (không phải kênh khác), và phải là referee (host luôn là referee, xem `Match.refs`) hoặc `TOURNEY_MANAGER` — TRỪ `mp_help` được miễn check cuối này (2 check đầu vẫn áp dụng).

**Quyết định thiết kế (đã hỏi advisor)**: KHÔNG sửa `CommandDispatcher`/`ICommand` hiện có. Thay vào đó tạo `MpCommandDispatcher : ICommand` với `Trigger = "mp"`, `RequiredPriv = Unrestricted` (để tầng ngoài không bao giờ chặn quá sớm — toàn bộ gate thật nằm bên trong), đăng ký như 1 `ICommand` bình thường nên cắm thẳng vào `IEnumerable<ICommand>` sẵn có không cần đụng gì khác. Bên trong `MpCommandDispatcher.HandleAsync`: tự parse subcommand (mặc định `"help"` nếu rỗng), tra `Dictionary<string, IMpSubCommand>` (interface mới, giống `ICommand` nhưng nhận `MpCommandContext(Player, Args, Match)` thay vì `CommandContext` — có sẵn `Match` không cần suy ra lại), rồi áp NGUYÊN 3 check của `ensure_match` MỘT LẦN ở tầng dispatcher (không lặp lại ở từng subcommand, vì cả 24 lệnh đều check giống hệt nhau) — trừ check referee được bỏ qua nếu subcommand là "help".

**Đã làm (commit sắp tới, slice 1/3 — "chứng minh cơ chế hoạt động")**:
- `IMpSubCommand`/`MpCommandContext` (`Commands/IMpSubCommand.cs`).
- `MpCommandDispatcher` (`Commands/MpCommandDispatcher.cs`) — implement `ICommand`, hoist 3-check `ensure_match`.
- `MpHelpCommand` (`Commands/MpHelpCommand.cs`) — port `mp_help`, liệt kê subcommand có doc + đủ priv, dùng `IServiceProvider.GetServices<IMpSubCommand>()` lazy (tránh vòng lặp DI y hệt `HelpCommand`).
- Đăng ký DI: `MpCommandDispatcher` là `ICommand`, `MpHelpCommand` là `IMpSubCommand`.
- Cập nhật doc comment `HelpCommand.cs`: không còn "bỏ qua command sets" nữa — vì `MpCommandDispatcher` đăng ký như `ICommand` bình thường nên tự động xuất hiện trong `!help` (dòng `!mp: Multiplayer commands.`) mà không cần thêm section riêng như Python.
- Test: `MpCommandDispatcherTests` (9 test: mặc định help khi rỗng args, không ở trong trận → null, gửi sai kênh → null, không phải referee/tourney manager → null, host luôn được coi là referee, tourney manager bỏ qua check referee, help bỏ qua check referee, subcommand không tồn tại → null, thiếu priv subcommand → null, args được cắt đúng khi truyền xuống subcommand) + `MpHelpCommandTests` (1 test). Application.Tests 382/382, ArchitectureTests 9/9, `Infrastructure.Tests` 116/116 (composition root resolve sạch, không đổi số packet handler vì `MpCommandDispatcher` không phải packet handler).

## Kế hoạch tiếp theo cho `!mp`/`!pool` (đã thống nhất với advisor)

Chia theo phụ thuộc, KHÔNG theo thứ tự liệt kê trong Python:

1. **Hạ tầng dispatch + `ensure_match` + `help`** — xong, xem trên.
2. **Các lệnh chỉ cần thứ đã có sẵn** (`MatchSession`/`MatchMembershipService`/scrim engine, KHÔNG cần gì mới): `start, abort, map, mods, freemods, host, randpw, invite, addref, rmref, listref, lock, unlock, teams, condition, scrim, endscrim, rematch, force`. Đây là chỗ `!mp scrim`/`!mp endscrim` LẦN ĐẦU TIÊN nối `MatchScoringService`/`IsScrimming` vào 1 lệnh chat thật — commit trả (payoff) cho việc xây engine trước đó.
3. **Mappool** (`loadpool, unloadpool, ban, unban, pick` trong `!mp`, và toàn bộ 7 lệnh `!pool`) — CẦN 1 tầng Domain/Infrastructure hoàn toàn mới (repository `tourney_pools`/`tourney_pool_maps`, đã `grep` xác nhận `TourneyPool` KHÔNG tồn tại ở đâu trong bancho-net). Đây là quyết định phạm vi giống hệt câu hỏi scrim trước đây ("cần scrim" KHÔNG có nghĩa transitively "cần luôn tầng lưu trữ mappool") — theo đúng lời advisor: KHÔNG tự ý xây `ITourneyPoolRepository` dựa trên suy đoán, để dành hỏi user tại đúng điểm rẽ này (sau khi xong slice 2), không hỏi trước/chặn tiến độ của slice 2 vì nó không phụ thuộc gì vào câu trả lời.

Đang làm slice 2 (room-control commands), sẽ tách thành 1 commit riêng theo đúng "nhiều commit nhỏ, không marathon".

## Slice 2: room-control `!mp` commands — đã tư vấn advisor lần 2, phát hiện 1 rủi ro concurrency thật

Trước khi viết code, hỏi advisor để chốt 2 việc: (1) `!mp start <seconds>`/`!mp start cancel` cần timer huỷ được (Python dùng `loop.call_later`) — advisor xác nhận: đã tự quyết định hoãn việc này ngay trong note.md trước đó rồi, giữ nguyên quyết định, KHÔNG xây `IScheduler` mới, chỉ trả về thông báo "chưa hỗ trợ" cho `N`/`cancel`; (2) **rủi ro thật của slice này là VỊ TRÍ GIỮ LOCK, không phải scheduler** — mọi subcommand mutate state trận đấu (lock/unlock/teams/mods/freemods/host/condition/...) đua với packet handler nếu không giữ `match.Lock`, và test đơn luồng KHÔNG bắt được lỗi này (đúng loại lỗi mà race test trước đây tồn tại để chứng minh). Advisor dặn: **KHÔNG khoá 1 lần ở tầng `MpCommandDispatcher`** (bọc quanh `subCommand.HandleAsync`) — vì `MatchMembershipService.Join` (dùng bởi `mp_force`) giả định NGƯỜI GỌI đã giữ sẵn lock (đọc code xác nhận: `Join`'s doc comment "Caller must already hold match's Lock"), nên khoá kép sẽ deadlock (`SemaphoreSlim` không reentrant). Quyết định: mỗi subcommand mutate state TỰ giữ `match.Lock` (giống hệt pattern packet handler), không có khoá chung ở dispatcher; `mp_map` fetch beatmap TRƯỚC khi giữ lock (không I/O dưới lock, cùng nguyên tắc với `MatchScoringService`).

**Refactor nhỏ trước khi viết `MpStartCommand`**: `MatchStartHandler` (packet MATCH_START) inline y hệt logic `Match.start()` (set slot Playing/InProgress, gửi MatchStart+enqueue_state) — trích thành `MatchMembershipService.Start(match)` dùng chung cho cả packet handler và `!mp start`/`!mp force-start`, đúng như Python gọi chung `match.start()`. Test `MatchStartHandlerTests` (2 test) vẫn xanh sau refactor.

**Đã làm (15 lệnh room-control, KHÔNG gồm scrim/endscrim/rematch — để dành slice 3 làm commit trả cho scrim engine)**: `MpStartCommand` (bỏ nhánh delay-N-giây/cancel, trả thông báo "chưa hỗ trợ"), `MpAbortCommand`, `MpMapCommand` (fetch trước lock), `MpModsCommand`, `MpFreemodsCommand`, `MpHostCommand`, `MpRandpwCommand` (`RandomNumberGenerator.GetHexString(16, lowercase: true)` thay `secrets.token_hex(8)`), `MpInviteCommand`, `MpAddRefCommand`, `MpRmRefCommand`, `MpListRefCommand`, `MpLockCommand`, `MpUnlockCommand`, `MpTeamsCommand`, `MpConditionCommand` (bỏ hẳn nhánh đặc biệt "pp" — theo quyết định no-pp toàn dự án, trả thông báo rõ ràng thay vì cho phép), `MpForceCommand` (Administrator, hidden, gọi thẳng `MatchMembershipService.Join` với `match.Password` của chính trận để bypass mật khẩu — khớp Python).

**2 chỗ khác Python có chủ đích, AN TOÀN HƠN (không phải bug)**:
- `mp_mods`/`mp_freemods`: Python `assert slot is not None` khi lấy slot của người gọi/host — sẽ crash nếu 1 tourney manager chạy lệnh mà không có slot nào trong trận (referee thường CÓ slot vì `mp_addref` bắt buộc, nhưng tourney manager thì không chắc). Bản port bỏ qua việc set slot mods nếu không tìm thấy slot, không crash.
- `mp_rmref`: sửa lại text thông báo lỗi cú pháp từ đúng-y-hệt-Python (Python dùng nhầm message của `addref`: `"Invalid syntax: !mp addref <name>"`) thành `"Invalid syntax: !mp rmref <name>"` — rõ ràng là lỗi copy-paste bên Python, không phải hành vi cố ý cần giữ nguyên.

**Lưu ý về `IMpSubCommand.Hidden` (theo đúng lời advisor, chấp nhận không xử lý)**: outer `CommandDispatcher` chỉ đọc `Hidden` của `MpCommandDispatcher` (hardcode `false`) sau khi `HandleAsync` chạy xong — `Hidden` của TỪNG subcommand (chỉ `mp_force` có `hidden=true` trong slice này) không có đường nào lan ra ngoài để cho tầng gọi biết "câu trả lời này nên ẩn khỏi kênh, chỉ hiện cho staff". Ảnh hưởng thấp (chỉ mỗi `mp_force`'s "Welcome." bị hiện công khai thay vì ẩn) và việc lan truyền đúng đòi hỏi đổi shape trả về của `HandleAsync` cho toàn bộ subcommand — không đáng làm cho 1 lệnh admin, chấp nhận sai khác này.

**Bug phát hiện khi viết test**: `MultiplayerTestSupport.Fixture.RegisterAll` chỉ stub `GetById`, thiếu `GetByName` — mọi lệnh dùng `sessionRegistry.GetByName` (host/invite/addref/rmref/force) đều fail test với "Could not find a user by that name." dù test giả lập gọi đúng tên. Sửa `RegisterAll` thêm `SessionRegistry.GetByName(session.Name).Returns(session)`. (Không phải bug trong code sản phẩm, chỉ là thiếu sót của helper test dùng chung.)

**Test**: 12 file test mới, 59 test (bao test invalid-syntax, not-found, các nhánh chính + 1 nhánh an toàn hơn cho `mp_mods`). Application.Tests 430/430, ArchitectureTests 9/9, Infrastructure.Tests 116/116 (composition root resolve sạch, tổng cộng giờ có 17 `IMpSubCommand` đăng ký).

## Slice 3: scrim-control `!mp scrim`/`!mp endscrim`/`!mp rematch` — commit trả cho scrim engine

Đây là chỗ `MatchScoringService`/`IsScrimming`/`WinningPoints`/`MatchPoints`/`Winners` (xây trong 1 commit trước, chưa có đường nào từ client thật bật lên) LẦN ĐẦU TIÊN được nối vào 1 lệnh chat thật. Cả 3 lệnh đều mutate state trận đấu nên đều tự giữ `match.Lock` (đúng pattern đã chốt ở slice 2) — kể cả `mp_scrim`/`mp_endscrim` dù Python không khoá gì (không có lock nào cả), vì các field này cũng được `MatchScoringService` đọc/ghi (dưới lock ngắn ở cuối `UpdateMatchPointsAsync`) từ 1 task nền — không khoá ở đây sẽ tạo đúng loại race mà lock tồn tại để chặn.

**Phát hiện thú vị khi viết test cho `mp_scrim`**: đọc kỹ công thức `winning_pts = (best_of // 2) + 1` với ràng buộc `0 <= best_of < 16` (đã check trước đó) — nhánh "đặt về 0 để huỷ scrim" (`if winning_pts != 0: ... else: ... "Scrimming cancelled."`) trong Python là **CODE CHẾT**: với `best_of` trong khoảng 0-15, `winning_pts` LUÔN LUÔN >= 1, không bao giờ bằng 0 — nên "!mp scrim 0" không bao giờ chạy tới nhánh huỷ, mà bị chặn ở check "Best of must be an odd number!" (0 là số chẵn) trước khi tới đó. Đây rõ ràng là tàn dư từ 1 phiên bản công thức cũ (có thể trước đây là `best_of // 2` không có `+1`) mà tác giả gốc không dọn nhánh else khi sửa công thức. **Port NGUYÊN VĂN, không "sửa"** — vì `!mp endscrim` đã là cách chính thức để huỷ scrim, nhánh else chỉ là code thừa vô hại, không phải bug ảnh hưởng hành vi thật. Test ban đầu của tôi giả định sai (tưởng "0" sẽ huỷ scrim) — bị test tự bắt lỗi ngay, sửa lại theo đúng hành vi thực tế (xem comment trong `MpScrimCommandTests.cs`).

**Đã làm**:
- `MatchSession` thêm `DecrementMatchPoint`/`PopLastWinner` (port `match.match_points[x] -= 1`/`match.winners.pop()`, dùng bởi `mp_rematch`).
- `MpScrimCommand` (aliases: autoref) — port `mp_scrim` nguyên công thức `BEST_OF` regex (`^(?:bo)?(\d{1,2})$`).
- `MpEndScrimCommand` (aliases: end).
- `MpRematchCommand` (aliases: rm) — deduct điểm + pop winner khi rollback; message hiển thị tên người chơi (hoặc "player #id" nếu offline) hoặc "Blue"/"Red" cho team, đơn giản hơn 1 chút so với Python (Python interpolate thẳng object Player/MatchTeams vào f-string, cho ra text xấu tuỳ `__repr__` — bản port chủ động format cho dễ đọc, không phải divergence hành vi quan trọng).

**Test**: 3 file test mới, 15 test. Application.Tests 445/445, ArchitectureTests 9/9, Infrastructure.Tests 116/116 (20 `IMpSubCommand` đăng ký, không đổi số packet handler).

## Còn lại cho Phase 7 (đã lỗi thời — xem đảo ngược lớn ngay dưới đây)

Tổng cộng đã port 20/25 lệnh `!mp` (thiếu `loadpool`, `unloadpool`, `ban`, `unban`, `pick` — 5 lệnh còn lại đều cần mappool) + 0/7 lệnh `!pool`. Đã hỏi user tại điểm rẽ mappool, user chọn "Cần ngay bây giờ" — nhưng NGAY SAU ĐÓ user đổi ý hoàn toàn, xem bên dưới.

## QUYẾT ĐỊNH LỚN CỦA USER: bỏ toàn bộ bot commands + BanchoBot (đảo ngược phần lớn Phase 7 vừa xây)

Ngay khi tôi vừa bắt đầu đọc Python source cho tầng Mappool (`app/repositories/tourney_pools.py`, `tourney_pool_maps.py`, `pool_*` command bodies), user gửi 2 tin nhắn liên tiếp, KHÔNG liên quan gì đến câu hỏi mappool vừa hỏi:

1. *"tôi muốn trước mắt hãy loại bỏ bot commands đi, tôi sẽ implement sau - chỉ giữ chức năng server chứ chưa implement bot commands"*
2. *"loại bỏ banchoBot luôn"*

Đã hỏi lại 2 lần qua AskUserQuestion để xác định đúng phạm vi (không đoán mò vì đây là việc xoá code đã test/commit, không phải thêm mới):
- Câu 1: "bot commands" là TOÀN BỘ hệ thống lệnh chat (kể cả `!help`/`!roll`/`!block`/`!unblock`/`!reconnect`/`!changename`/`!apikey` từ Phase 4, KHÔNG chỉ `!mp`/`!pool` tôi mới xây session này) hay chỉ phần mới? → User chọn **"Toàn bộ hệ thống lệnh chat"**.
- Câu 2 (sau khi thấy BanchoBot cũng gắn với scrim engine): scrim engine (`MatchScoringService`, xây trước đó, dùng `SendBot` để thông báo — giờ không còn BanchoBot để gửi) nên xoá luôn hay chỉ bỏ phần thông báo? → User chọn **"Xóa luôn scrim engine"**.

**Lý do quyết định này hợp lý, không phải tuỳ hứng**: đọc lại `Program.cs`'s comment cũ tự nó đã nói rõ — "BanchoBot gets a real, permanent session ... so command responses and PM auto-replies have somewhere to originate from" — nghĩa là TOÀN BỘ lý do BanchoBot tồn tại là để phục vụ bot commands. Bỏ bot commands mà giữ BanchoBot thì BanchoBot vô dụng. Và scrim engine chỉ có 1 điểm vào (`!mp scrim`) và 1 cách thông báo (`SendBot`) — cả hai đều mất, engine trở thành code chết hoàn toàn, không có ích gì để giữ lại (implement lại cùng lúc với bot commands sau này).

### Phạm vi đã xoá (commit tiếp theo)

**Xoá hoàn toàn**:
- `src/Bancho.Application/Commands/` (31 file: `ICommand`, `ICommandDispatcher`, `CommandDispatcher`, `CommandTargetResolver`, `HelpCommand`, `RollCommand`, `BlockCommand`, `UnblockCommand`, `ReconnectCommand`, `ChangeNameCommand`, `ApiKeyCommand` — TẤT CẢ từ Phase 4, cộng toàn bộ 20 `Mp*Command` + `MpCommandDispatcher` + `IMpSubCommand` xây trong session này) + `tests/Bancho.Application.Tests/Commands/` (29 file test tương ứng).
- Scrim engine: `MatchScoringService.cs`, `ScrimParticipant.cs`, `MatchRoundSnapshot.cs`, field/method scrim trong `MatchSession` (`IsScrimming`, `WinningPoints`, `MatchPoints`/`GetMatchPoints`/`AddMatchPoint`/`DecrementMatchPoint`, `Bans`/`AddBan`/`RemoveBan`, `Winners`/`RecordWinner`/`PopLastWinner`, `ResetScrim`), `PlayerSession.RecentScore`/`RecentScoreSnapshot`, dòng gán `RecentScore` trong `ScoreSubmissionUseCase`, nhánh `is_scrimming` trong `MatchCompleteHandler`.
- BanchoBot: bootstrap session trong `Program.cs` (block "BanchoBot gets a real, permanent session..."), `PlayerSession.IsBotClient` (field + constructor param + check trong `Enqueue`), `MatchMembershipService.SendBot`, lời chào/thông báo restricted "từ BanchoBot" trong `OsuLoginUseCase` (+ `WelcomeMessage()`/`RestrictedMessage` không còn dùng, kéo theo `discordOptions` param không còn dùng — XOÁ, nhưng **KHÔNG xoá `DiscordOptions.cs`** vì nó còn field `AuditLogWebhookUrl` không liên quan BanchoBot, là config scaffolding chờ tính năng audit-log riêng).

**Sửa (bỏ nhánh bot, giữ lại phần còn lại)**:
- `SendPublicMessageHandler`/`SendPrivateMessageHandler`: bỏ hẳn việc gọi `ICommandDispatcher` — tin nhắn bắt đầu bằng `!` giờ chỉ là chat thường, broadcast như mọi tin nhắn khác. PM tới bot (không còn tồn tại) không còn là trường hợp đặc biệt nữa — mọi PM đều đi qua `DeliverToRealTargetAsync` bình thường.
- `MatchInviteHandler`/`FriendAddHandler`/`FriendRemoveHandler`: bỏ check `target.IsBotClient` (field không còn tồn tại).
- `CreateMatchHandler`: bỏ dòng `SendBot(match, "Match created by X.")`.
- `MatchCompleteHandler`: bỏ tham số `MatchScoringService`, bỏ đoạn build `MatchRoundSnapshot`/tính `wasPlaying` (giữ nguyên `notPlaying` vì dùng cho `immune:` — KHÔNG phải riêng scrim).

**Việc CHƯA xoá, cân nhắc rồi giữ lại vì KHÔNG phải bot commands**:
- `MatchSession.Referees`/`IsReferee`/`AddReferee`/`RemoveReferee`/`TourneyClients` — vẫn dùng bởi `MatchMembershipService.Leave` (dọn referee khi rời trận) và `TourneyMatchJoinChannelHandler`/`TourneyMatchLeaveChannelHandler` (packet thật, không phải lệnh chat).
- `MatchSession.Url`/`Embed` — dùng bởi `MatchInviteHandler` (packet MATCH_INVITE thật, không phải `!mp invite`).
- Seed data "BanchoBot" (user id=1) trong `001_base.sql` và test `SqlMigrationRunnerTests` xác nhận migration đúng — giữ nguyên, chỉ là dữ liệu DB không hại gì, có thể tái dùng nếu bot quay lại sau này.
- `DiscordOptions.cs`/DI registration/config-binding test — giữ nguyên, không liên quan BanchoBot.

**Bug nhỏ gặp phải khi sửa `Program.cs`**: xoá bootstrap bot làm `IUserRepository`/`ITokenGenerator`/`Bancho.Domain` (using) không còn dùng trong file — dọn theo, build lại xác nhận 0 warning.

**Kết quả sau khi sửa xong**: build sạch toàn solution (Application/Infrastructure/Web/tất cả test project), Application.Tests 316/316 (giảm từ 445, đúng theo số lượng ~130 test bị xoá cùng code), ArchitectureTests 9/9, Infrastructure.Tests 116/116 (composition root không còn `ICommand`/`IMpSubCommand`/`MatchScoringService` nào, chỉ còn packet handler + use case thật).

## Còn lại cho Phase 7 (sau đảo ngược)

Chat command layer (bao gồm cả `!mp`/`!pool`/scrim) và BanchoBot đều bị hoãn — user sẽ tự implement lại sau, không nằm trong phạm vi các phase tiếp theo trừ khi user yêu cầu lại. Server multiplayer packet-level protocol (CREATE_MATCH, JOIN_MATCH, MATCH_* packets, referee/tourney-client tracking) vẫn nguyên vẹn, hoạt động độc lập với lớp lệnh chat đã bỏ.

**HTML pages `/matches`/`/online` — user quyết định BỎ HẲN, không port.** Đã giải thích rõ 2 trang này là gì (port từ `app/api/domains/cho.py`'s `bancho_view_online_users`/`bancho_view_matches` — trang debug monospace, không phải client osu! gọi tới, chỉ để admin xem nhanh qua trình duyệt: `/online` liệt kê người chơi đang online, `/matches` liệt kê trận đang diễn ra + host/beatmap) rồi hỏi user có muốn port không — user trả lời "loại bỏ". Chưa có route/stub nào cho 2 trang này trong bancho-net (xác nhận qua grep), nên không có gì cần xoá, chỉ đơn giản là KHÔNG làm.

**Phase 7 coi như đã xong** với phạm vi hiện tại: toàn bộ packet-level multiplayer protocol (tạo/vào trận, quản lý slot, referee/tourney-client tracking) đã port xong; phần còn lại trong kế hoạch gốc (chat commands, mappool, scrim, HTML debug pages) đều bị hoãn/bỏ theo quyết định của user.

## Phase 8 — thu hẹp phạm vi ngay từ đầu, rồi định hướng lại toàn bộ dự án

User xác nhận chuyển sang Phase 8, tôi liệt kê danh sách endpoint. User trả lời ngay: bỏ (trả về không xử lý) `screenshot upload/view`, `getfriends` (bỏ luôn tính năng bạn bè), `favourites`, `rate`, `comments`, `/users registration`, `difficulty-rating redirect`.

**Phát hiện khi hỏi rõ "getfriends (bỏ tính năng bạn bè)"**: endpoint HTTP `osu-getfriends.php` (client tải danh sách bạn để hiển thị UI) tách biệt với 2 packet handler `FriendAddHandler`/`FriendRemoveHandler` (xử lý khi người chơi bấm thêm/xoá bạn qua UI client, ghi `relationships` DB) — đã xây từ Phase 3/4, đang hoạt động. Hỏi lại user có muốn bỏ luôn 2 packet handler này không → **user chọn bỏ luôn**. Đã xoá `FriendAddHandler.cs`/`FriendRemoveHandler.cs` + test tương ứng, bỏ 2 dòng đăng ký DI, sửa `CompositionRootTests` handler count 44→42. Build/test lại: Application.Tests 311/311, Infrastructure.Tests 116/116 (composition root xác nhận đúng 42 handler).

**Ngay sau đó, user nêu định hướng lớn hơn hẳn**: *"tôi muốn thiết lập hệ thống chỉ để phục vụ multiplayer và tourney nên các tính năng ngoài lề cần loại bỏ"* — nghĩa là mục tiêu cuối của bancho-net KHÔNG PHẢI port toàn bộ bancho.py thành 1 server osu! đầy đủ tính năng (profile/leaderboard xã hội/clan/friends/comments/ratings/...), mà là 1 server THU GỌN chỉ phục vụ multiplayer + giải đấu. Đây là thay đổi định hướng ảnh hưởng ngược lại toàn bộ kế hoạch còn lại (Phase 8/9/10/11 trong `glowing-snacking-hickey.md`), KHÔNG chỉ riêng Phase 8. Cần làm rõ với user CHÍNH XÁC ranh giới "multiplayer + tourney" bao gồm/loại trừ gì trước khi tiếp tục port bất kỳ endpoint nào nữa, tránh vừa xây vừa phải xoá lại như đã xảy ra với bot commands/BanchoBot/scrim.
