# User Privileges

## Overview

Basil grants access as a single bitfield (`Basil.Domain.Users.Privileges`) rather than a role name, so a user can hold any combination of flags at once. A moderator who's also a tournament manager is just two bits set on the same field, not a separate role to define.

## Flag reference

| Flag | Bit | Value | Consumer(s) | Notes |
|------|-----|-------|-------------|-------|
| `Unrestricted` | 0 | 1 | Login, match join, chat, score submission | **Core flag.** A user without this is "restricted": cannot play multiplayer, send chat, or submit scores. |
| `Verified` | 1 | 2 | Login | Auto-granted on first successful login (`LoginService`). Gates no specific feature by itself but is expected to be present for normal operation. |
| `Whitelighted` | 2 | 4 | None | Legacy bancho.py port. No consumer in this codebase (no-op). |
| `Supporter` | 4 | 16 | Login (via `Donator`) | osu! supporter badge shown in client. Combined with `Premium` → `Donator`. |
| `Premium` | 5 | 32 | Login (via `Donator`) | Legacy, no direct consumer. |
| `Alumni` | 7 | 128 | None | Legacy bancho.py port. No consumer (no-op). |
| `TourneyManager` | 10 | 1024 | `!mp` subcommands | Allows tournament management commands in multiplayer matches. |
| `Nominator` | 11 | 2048 | None | Legacy bancho.py port. No consumer (no-op). |
| `Moderator` | 12 | 4096 | Channel access, match join, IRC | Can read/write `#staff` channel. Bypasses match password check (`MatchMembershipService`). Gets `@` prefix in IRC (`IrcAuthenticationService`). |
| `Administrator` | 13 | 8192 | Channel access, match join, IRC | Same as `Moderator` for channel/IRC purposes. |
| `Developer` | 14 | 16384 | Channel access, match join, IRC | Same as `Moderator` for channel/IRC purposes. |

## Composite flags

| Name | Value | Bitwise composition |
|------|-------|---------------------|
| `Donator` | 48 | `Supporter \| Premium` (16 \| 32) |
| `Staff` | 28672 | `Moderator \| Administrator \| Developer` (4096 \| 8192 \| 16384) |

## Default privilege on account creation

When a new user is created (whether through in-game registration, `POST /users` on the `osu.` host with a matching `AdminKey`, or the admin API, `POST /users` on the `api.` host), the default privilege set is:

```
Unrestricted | Verified | Supporter  (value 19)
```

If `privilege` is explicitly provided to the admin API, that value is used instead. In-game registration always uses the default.

## Login auto-grant

On every successful login, if the user does not yet have `Verified` set, the server adds it:

```csharp
if ((user.Privilege & Privileges.Verified) == 0)
    newPrivilege = user.Privilege | Privileges.Verified
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

## See also

- [`authentication.md`](authentication.md): where `Verified` gets auto-granted
- [`run-deployment.md`](run-deployment.md): creating the first account and granting staff privileges
