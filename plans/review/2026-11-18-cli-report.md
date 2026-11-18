# Basil dashboard trial, part 2 (tmux only, no repository file changed)

Server restored at the end: tmux session `basil`, `--console 0`, ports 443 and 16667 listening.

## Problems, most serious first

1. **Stuck modal dialog (verified).** Opened Users > trial1 > Privileges (a dialog), then pressed `F1` while it was open. The tab switched but the dialog stayed painted on top of every tab (Console, Monitor, Users). `Esc`, `Alt+Left`, `Enter`, `Tab`, `Shift+Tab`, `x`, `q`, `Ctrl+G` did not close it. Typed text no longer reached the command box (`status`, `shutdown --now` were swallowed or mangled; the box held only `xq`). Only `F`-keys and `Ctrl+C` still worked. The dashboard had to be shut down to recover.
2. **`restart` and `shutdown` ask a confirmation that is never shown (verified).** Typed `restart` + Enter: the screen showed only `❯ restart` and the log line `Console command: restart`, no prompt, no hint. The box then silently acts as the answer line: I typed `shutdown` and the dashboard printed `Cancelled.` under the `restart` line (my `shutdown` was taken as the answer "no"). Typing `shutdown` then `n` printed `Cancelled.` and then `error: invalid: Unknown command: n. Type help for the list.` Only `restart` + `yes` worked. console.md says the command "asks for confirmation" but not how it looks or what to type.
3. **Terminal not restored after Ctrl+C shutdown (verified).** After the second Ctrl+C the pane kept the whole last dashboard frame (tab strip, old list, both toasts) and the shell prompt was glued onto a log line: `Shutdown step write the queued log events done in 0 ms[haith@Mocha osuBasil]$`. The alternate screen was off, but nothing was cleared and no "shutdown complete" line was shown.
4. **`--ascii` still draws non-ASCII (verified).** With `--ascii`, per tab: F1 `↑ ↓ │`; F2 `█ ░` and frames; F3 `╭ ╮ ╰ ╯ ─ │ █ ░ – ↑ ↓`; F9 and F6 full box frames. The outer frame is `+---+`/`|` but the inner frames are still box-drawing. `capture-pane | grep -P '[\x{2500}-\x{28FF}]'` matched 41 lines on F3, and 352-507 characters on every list tab. console.md: "draws with ASCII characters only (no box-drawing or braille)".
5. **Privileges dialog cannot be changed by keyboard (verified).** Users > trial1 > Privileges lists `☑ Player ☐ Moderator ☑ Supporter ...`. `Space`, a literal space, `Enter`, `Down`, `Up`, `Tab`, `Shift+Tab` never changed a box (checked after each key). No focus marker is visible on any box. `Tab` moves to `OK` and `Cancel` only. Esc closed it (when no F-key had been pressed). So this action cannot be done without the mouse.
6. **Detail actions: arrow keys do nothing (verified).** console.md says `Left`, `Right`, `Tab` move between actions. `Right`, `Left`, `Down`, `Up` did not move the focus in the Users detail panel; only `Tab` does. Also the first `Tab` from the list moves through the search/list panes before reaching the first action (about 4 presses), and a focused red button (Remove avatar, Delete account, Close room) shows no underline, so the user cannot see which one is focused.
7. **Raw exception text in a toast (verified).** Users > Set avatar with a missing file: toast `✗ Could not find a part of the path '…`, console `error: invalid: Could not find a part of the path '/nonexistent/x.png'.` (a .NET message; the cut hides the path). The dialog also closes on the failure, so the input is lost.
8. **Lists show about 7 rows in a tall frame (verified).** On Users, Beatmaps, Lobby the list uses 7 rows while 15-30 empty rows stay below, at 160x45 and at 80x24. The footer says `show 1–6 in 9` (Users, 7 rows visible) and `show 1–7 in 26`, and after scrolling to the end it still says `show 1–6 in 9`. The Users header said `Offline (10)` while 7 rows were visible until scrolling (Down x10 revealed ids 11 and 12).
9. **Ctrl+C toast is cut (verified):** `! Press Ctrl+C again within 3 seconds…`; the consequence (shutdown) is cut off by the 40-column toast.
10. **Success toasts say little (verified):** `✓ Updated.` (Edit profile, Set private, Beatmaps), `✓ Done: lock.`, `✓ Done: close.` (rooms), `✓ Silenced.` (no duration/user). A failed action in a dialog leaves nothing on the dialog; only the toast.
11. **Doc mismatches (verified):** a `Close` button exists in every detail panel (console.md: "There is no Close button"). Enter on a Chat member opens the account detail (console.md: opens a direct message). The channel detail (`[Channel]` on the frame) could not be reached by keyboard. `F1` is accepted while a dialog is open (see 1). The Settings > Server key hints line is cut at 60 columns (`... Enter save · Esc`).
12. **Likely:** Users > bob > Sessions shows Irc and Api sessions as `Online` (since 2026-11-17) for a user whose detail says `Connections offline`, and the server had logged `Closed 2 connections left open` at start. Probably history rows labelled Online, but it contradicts the user line.
13. **Likely:** room ids differ between the open list and the closed list: `rooms create --name Trial` answered `Opened room 1.` while the closed list shows that room as id 14 (and `Trial2` as 15 after it closed, `1` while open).
14. Scrollbar thumb is drawn on a one-row list and overlaps the right column header (`Connections█`) (verified, cosmetic).

## Step 2: detail panels and actions

Detail opens with Enter on a row (list focused). Move focus into the panel with `Tab`; focused action = underlined/blue. Every dialog action runs a console command, logged as `Console command: ...` (passwords masked as `***`), and a toast follows. Judgments: S = server answered, T = answer states what happened, N = user knows what to do next.

Users (test user `trial1` created with `users create`, bob used for the invalid case):
* Sessions… opens a nested list (`Users › bob › Sessions`, `[← Back]`, groups Stable/Tourney/Irc/Api). `Esc` returns. OK.
* Edit profile (bob) with Country `VNXX` + OK: toast `✗ --country must be a two-letter code.`, console `error: invalid: --country must be a two-letter code.` (S yes, T yes, N yes). Away message and Block PMs (yes/no) fields are shown empty although bob has values.
* Change password: field masked `•••`; OK gives `✓ Updated.`; log `users 13 set --password ***` (a 3-character password was accepted). T weak (not saying password).
* Privileges: see problem 5. Nothing could be changed; not verified end to end.
* Silence (Duration `30m` default, Reason `test`): `✓ Silenced.`; `users 13` then showed `silencedUntil: 2026-11-18 14:02:04`. Button became `Unsilence`; pressing it: `✓ Silence lifted.`. Really happened.
* Set avatar: missing path see problem 7; a valid 8x8 PNG: `✓ Avatar set.`.
* Remove avatar: confirmation `Remove the avatar of trial1 (id 13)? The default image is shown instead.` then `✓ Avatar removed.`.
* Delete account: confirmation `Delete user trial1 (id 13)? The account is hidden from sign-in and every listing, its privileges are cleared and every session is closed, which disconnects the client. Its scores are kept.`, then `✓ User deleted.`; `users 13` then `error: notFound: No such user.`. Really happened.
* Scores…, Friends…, Blocks… were not opened.

Lobby (rooms):
* A closed room offers only `Creator…` and `Close` (the panel close). Fine.
* Open room `Trial2` (created with `rooms create --name Trial2`) offers Creator…, Chat…, Lock, Settings ›, Manage ›, Close room, Close. Lock: `✓ Done: lock.`; state became `locked` and the button `Unlock`. Settings › opens a menu (Rename, Size, Teams and winning, Password, Make private, Beatmap, Mods, Cancel); `Enter` on it did nothing visible, `Tab` then `Enter` closed it; I could not select an item (not verified). Manage › not opened. Close room: confirmation `Close room 1 (Trial2)? The room ends and its 0 player(s) are removed from it. It cannot be reopened.`, then `✓ Done: close.`; log `Room 1 closed`.

Chat: `Enter` on `#lobby` focuses the message box; `hello trial` + Enter appeared as `13:37 BasilBot: hello trial` and the member count went 0 to 1. Enter on member `BasilBot` opens the account detail (Sessions…, Scores…, Friends…, Blocks…, Close), no direct message. Channel detail unreachable by keyboard (problem 11).

Beatmaps: set `1073074` (26 versions) detail offers Set status, Set private, Lock, Delete beatmapset, Close. Set status opens a menu (Pending, Ranked (now), Approved, Qualified, Loved, Cancel); cancelled with `Esc`, and that `Esc` also closed the detail. Set private: `✓ Updated.`, the log shows `beatmapsets 1073074 set --visible false` (wording "private" to `--visible false` is fine, but the toast does not say). The change was reverted with `beatmapsets 1073074 set --visible true`. Lock and Delete beatmapset not run.

Scores and Sessions: both tabs had no rows (`Scores (0)`, all four session groups 0), so no detail panel could be opened. Not tried.

## Step 3: Ctrl+C

Dashboard was in the stuck-dialog state of problem 1 for this test.
* Text `xq` in the box + Ctrl+C: the line was cleared, no toast. Matches console.md.
* Empty box + Ctrl+C: toast `! Press Ctrl+C again within 3 seconds…`. Waited over 3 s: the toast was gone; a further press only showed the toast again (window restarts). Matches.
* Two presses about 1 s apart: log `Shutting down`, the shutdown steps (`close the rooms`, `deliver the queued events`, `write the queued log events`), toast `✓ Shutting down the server...`. `run/exit.txt`: `EXIT=0`. Matches. Terminal not restored: problem 3. The shutdown worked even with the stuck dialog.

## Step 4: restart

`restart` + Enter: no prompt (problem 2). A second line of mine was consumed as the answer. After `restart` and `yes`: the log showed the new server booting (`13:39:12 ... Now listening on: https://0.0.0.0:443`) in the same pane; `run/exit.txt` held `EXIT=75`; `ss` showed both ports owned by a new `dotnet` process (parent PID 3506, i.e. orphaned from the wrapper), and `status` in the new dashboard answered. So a new server came up. The new process and the wrapper's `bash` both sit on the pane's tty (both listed as foreground): `Ctrl+C` twice then no longer stopped the new server (still running), and the first character of the next typed line was lost (`hutdown --now`, `error: invalid: Unknown command: hutdown`). I stopped it by killing the tmux session (the process then exited). This may be an artefact of the wrapper script, not of Basil.

## Step 5: --nocolor and --ascii

* `--nocolor`: `theme` printed `current: monochrome`; `theme "Dark"` printed `error: refused: The theme is fixed to monochrome by --nocolor.` The capture had no `38;`/`48;` colour escapes (0). Users and Monitor render. OK.
* `--ascii`: problem 4. The outer frame and the hints use `+ - |`, but inner frames, scroll bars and graphs keep non-ASCII characters on every tab except F10 (About) and F1 (one `│`).

## Step 6: small terminals

* 80x24: tabs F1 to F10 all render; the tab strip scrolls with `◀ ▶`; columns are dropped or cut with `…` (`Player, Moderator, Su…`); Chat is usable with two panes; no overlap and no crash. Monitor shows 2 graphs per row with a scroll bar.
* 60x20: all tabs render except Chat, which shows a centred box `Terminal too small` / `Current : 60 × 20` / `Needed  : 70 × 18  (Chat)` / `Enlarge the window or zoom out.` (matches console.md). Cut: the Settings key hint ends at `... Enter save · Esc`; Settings field names are cut with `…` (`Allowed browser or…`); Lobby loses the Host and Creator columns. Nothing overlaps or crashes.
* Back to 160x45: Chat and Users redraw at full size correctly.

## Step 7: mouse

Not tried (tmux).

## Step 8: restore

Dashboard stopped, `basil` session started with `--console 0`; `ss -ltnp | grep -E ':443|:16667'` shows both ports (pid 146063). The test data I left in the database: user `trial1` deleted (hidden), rooms Trial and Trial2 closed, bob's country unchanged, beatmapset 1073074 visibility restored.
