//! Thin C ABI surface around `akatsuki-pp` (osuAkatsuki's rosu-pp fork with relax/autopilot
//! support) so Bancho.Infrastructure can P/Invoke into the exact same difficulty-calculation
//! engine bancho.py uses via akatsuki-pp-py, without reimplementing star-rating math in C#.
//!
//! Scope note: bancho-net does not have a player-facing pp/performance system — leaderboards
//! rank by raw score (matching bancho.py's vanilla-mode `scoring_metric = "score"` path), and
//! only vanilla game modes are supported (no relax/autopilot). This crate therefore only exposes
//! *difficulty* (star rating), which is a property of the beatmap+mods and does not depend on
//! any player's hit results — not the full performance/pp calculation.
//!
//! Pinned dependency revision matches akatsuki-pp-py==1.0.5's Cargo.toml exactly, so computed
//! star ratings are bit-for-bit identical to what bancho.py would compute.

use akatsuki_pp::{Beatmap, BeatmapExt};
use std::ffi::CStr;
use std::os::raw::c_char;

#[repr(i32)]
pub enum BanchoDifficultyResult {
    Success = 0,
    NullBeatmapPath = -1,
    InvalidUtf8Path = -2,
    BeatmapParseFailed = -3,
}

/// Calculates the star rating for a beatmap file under the given mods. Returns 0 on success
/// (with `out_stars` populated) or a negative `BanchoDifficultyResult` error code.
///
/// # Safety
/// `beatmap_path` must be a valid, NUL-terminated UTF-8 C string. `out_stars` must be a valid,
/// non-null, writable `f64` pointer.
#[no_mangle]
pub unsafe extern "C" fn bancho_pp_calculate_difficulty(
    beatmap_path: *const c_char,
    mods: u32,
    out_stars: *mut f64,
) -> i32 {
    if beatmap_path.is_null() {
        return BanchoDifficultyResult::NullBeatmapPath as i32;
    }

    let path = match CStr::from_ptr(beatmap_path).to_str() {
        Ok(s) => s,
        Err(_) => return BanchoDifficultyResult::InvalidUtf8Path as i32,
    };

    let map = match Beatmap::from_path(path) {
        Ok(m) => m,
        Err(_) => return BanchoDifficultyResult::BeatmapParseFailed as i32,
    };

    *out_stars = map.stars().mods(mods).calculate().stars();

    BanchoDifficultyResult::Success as i32
}
