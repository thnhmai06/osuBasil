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
3. **Bỏ cơ chế snapshot/restore của Python** — thay bằng "tính xong mới ghi DB, cập nhật cache RAM sau khi ghi thành công". Điều này đúng cho phần cache trong RAM (không mutate trước khi xong nên không cần rollback). **NHƯNG khác với Python ở một điểm thật**: Python bọc `persist_submitted_score` + `persist_score_submission_stats` trong MỘT database transaction — insert score, update stats, demote previous-best cam kết atomic cùng lúc. Bản C# gọi `MarkPreviousBestScoresSubmittedAsync` → `CreateAsync` → `UpdateAfterScoreAsync` qua các connection/lệnh riêng biệt, KHÔNG có transaction bao ngoài. Nếu `UpdateAfterScoreAsync` lỗi sau khi `CreateAsync` đã thành công: previous-best đã bị demote xuống SUBMITTED, score mới đã lưu là BEST, nhưng stats KHÔNG được cập nhật — rscore delta của điểm đó mất vĩnh viễn (vì stats được ghi kiểu absolute-set từ cache + delta, không phải delta trực tiếp trong DB). Đây là edge case hiếm (lỗi DB giữa chừng lúc submit), nhưng là thật, chưa fix — cần transaction xuyên suốt nếu muốn khắc phục (refactor repo pattern hiện tại).
4. **Restrict-khi-thiếu-replay**: giữ kiểm tra hợp lệ (≥24 byte) + discard replay xấu, KHÔNG trừng phạt thật (chưa có hệ thống restriction) — đánh dấu `TODO(restriction-phase)` trong code. Điểm vẫn tính bình thường.
5. **Bỏ hẳn (deferred) khỏi core path vì không ảnh hưởng response cho client**: publish-user-stats broadcast, personal-best/first-place chat announce. Achievements đã bỏ từ đầu dự án (không phải quyết định riêng phase này).
6. **`validate_submission_integrity` thất bại không chặn submission** — y hệt Python hiện tại (đang "trial period", restrict bị comment out trong source gốc).
7. Thêm `ClientDetails` vào `PlayerSession` (chưa có từ Phase 3) — cần cho việc so khớp client_hash lúc submit.
8. Thêm `StorageOptions.ReplaysPath` (config mới, default `.data/osr`).
9. Format số trong charts response chưa khớp 100% cách Python format float (cosmetic, chưa rõ ảnh hưởng client thật).
10. `ScoreSubmissionUseCase`'s `ChecksumLocks` (`ConcurrentDictionary<string, SemaphoreSlim>`) không bao giờ evict — mỗi checksum duy nhất giữ 1 semaphore mãi mãi, unbounded growth. Nhỏ, không chặn, nhưng cần nhớ dọn sau (việc evict an toàn không tầm thường vì có thể race với request đang chờ lock).

## Còn lại cho bạn

Toàn bộ luồng submit CHƯA test với client osu! thật (cần replay hợp lệ + server local — xem kinh nghiệm `bancho_net_local_client_testing`). Nếu đồng ý các quyết định trên, có thể commit và tiếp tục Phase 7 (Spectator + Multiplayer — trọng tâm chính). Nếu có gì cần sửa, mọi thứ vẫn nằm trong working tree, chưa commit.
