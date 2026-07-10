# User Privileges

This document describes the server-side privilege system (`Basil.Domain.Users.Privileges`) — what each flag means and how it affects runtime behaviour.

## Flag reference

| Flag | Bit | Value | Consumer(s) | Notes |
|------|-----|-------|-------------|-------|
| `Unrestricted` | 0 | 1 | Login, match join, chat, score submission | **Core flag.** A user without this is "restricted" — cannot play multiplayer, send chat, or submit scores. |
| `Verified` | 1 | 2 | Login | Auto-granted on first successful login (`OsuLoginUseCase`). Gates no specific feature by itself but is expected to be present for normal operation. |
| `Whitelighted` | 2 | 4 | — | Legacy bancho.py port. No consumer in this codebase (no-op). |
| `Supporter` | 4 | 16 | Login (via `Donator`) | osu! supporter badge shown in client. Combined with `Premium` → `Donator`. |
| `Premium` | 5 | 32 | Login (via `Donator`) | Legacy, no direct consumer. |
| `Alumni` | 7 | 128 | — | Legacy bancho.py port. No consumer (no-op). |
| `TourneyManager` | 10 | 1024 | `!mp` subcommands | Allows tournament management commands in multiplayer matches. |
| `Nominator` | 11 | 2048 | — | Legacy bancho.py port. No consumer (no-op). |
| `Moderator` | 12 | 4096 | Channel access, match join, IRC | Can read/write `#staff` channel. Bypasses match password check (`MatchMembershipService`). Gets `@` prefix in IRC (`IrcAuthenticationService`). |
| `Administrator` | 13 | 8192 | Channel access, match join, IRC | Same as `Moderator` for channel/IRC purposes. |
| `Developer` | 14 | 16384 | Channel access, match join, IRC | Same as `Moderator` for channel/IRC purposes. |

## Composite flags

| Name | Value | Bitwise composition |
|------|-------|---------------------|
| `Donator` | 48 | `Supporter \| Premium` (16 \| 32) |
| `Staff` | 28672 | `Moderator \| Administrator \| Developer` (4096 \| 8192 \| 16384) |

## Default privilege on account creation

When a new user is created — whether through in-game registration (`POST /users` on `osu.` host with matching `AdminKey`) or the admin API (`POST /users` on `api.` host) — the default privilege set is:

```
Unrestricted | Verified | Supporter  (value 19)
```

If `priv` is explicitly provided to the admin API, that value is used instead. In-game registration always uses the default.

## Login auto-grant

On every successful login, if the user does not yet have `Verified` set, the server adds it:

```csharp
if ((user.Priv & Privileges.Verified) == 0)
    newPriv = user.Priv | Privileges.Verified
```

No other privileges are auto-granted at login (no special first-user treatment).

## Client-facing privileges

The server's `Privileges` flags map to client-facing `ClientPrivileges` (sent in bancho protocol packets):

| Server flag | Client flag |
|-------------|-------------|
| `Unrestricted` | `Player` |
| `Donator` | `Supporter` |
| `Moderator` | `Moderator` |
| `Administrator` | `Developer` |
| `Developer` | `Owner` |
