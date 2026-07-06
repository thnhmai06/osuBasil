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

## Việc CÒN LẠI cho Phase 7 (chưa bắt đầu)

`MatchSession` core + race test → 22 match packet → `!mp`/`!pool` → HTML pages. Đây là phần rủi ro cao nhất, cần phiên làm việc riêng, tập trung — không nên vội. Khi bắt đầu, nhớ: viết race test cho cơ chế đồng bộ TRƯỚC, port packet handler SAU.
