# Permissions and privileges

## Overview

Basil separates **permissions**, which authorize actions, from **privileges**, which describe roles the osu! client knows. A permission is the source of authority. `Privileges` is derived from effective permissions and grants nothing by itself.

This separation lets Basil suspend specific actions without inventing special account states, while still reporting the appropriate client-facing roles.

## Permissions

`Permissions` is a flat flags enum of independent actions, grouped in its source by resource. Examples include `RoomJoin`, `RoomManageAny`, `ChatPost`, `ChatMessageAnyone`, `UserRestrict` and `ServerConfigure`. A group of permissions is a mask in `PermissionsGroups`; groups are not extra enum members.

Some permissions include the bits of actions they imply. For example, `RoomManageAny` includes `RoomViewPrivate`, and `UserRestrict` includes the permissions to silence, add moderation notes and view login history. Checks ask for the smallest permission that allows the action. `Permissions.Allows(required)` succeeds only when every required bit is present; an empty requirement succeeds.

### Area managers

`PermissionsGroups.Areas` identifies the permissions in each area and, where one exists, the permission that manages that area:

| Area | Manager permission |
|---|---|
| Chat | `ChatMessageAnyone` |
| Multiplayer | `RoomManageAny` |
| Spectating | `RoomObserve` |
| Scores | None |
| Beatmapsets | `BeatmapsetManage` |
| Screenshots | `ScreenshotDelete` |

These manager permissions also determine which area permissions a user keeps while restricted. `PermissionsGroups.Default` is the initial permission set for a new account. Basil grants no extra permissions because an account is first or first to connect.

## Privileges are derived roles

`Privileges` contains the roles understood by osu!: `Player`, `Moderator`, `Supporter`, `Owner`, `Developer` and `Tournament`. It is computed from effective permissions and is neither stored nor configured. A role maps to the permissions it directly represents through `privilege.Permissions`; the role itself authorizes nothing.

The derived roles may be used where a client-facing role requirement is part of a channel's configuration. Such a requirement is met only when every requested role is present. Server-side actions must check `Permissions`, not infer authority from a reported role or a client's claim.

## Authorization and restrictions

Account actions are exposed through a `ScopedUser` bound to the actor's connection. Permission changes, restrictions and other caller-authorized operations use that scope instead of accepting an actor or permission mask at each step. The scope is short-lived, so its authority cannot outlive the request that created it.

### Effective permissions

A user's granted permissions are reduced by active restrictions:

```text
effective = granted permissions minus permissions suspended now
```

A `RestrictionRecord` suspends a permission mask from `StartsAt` through, but not including, `EndsAt`. A `null` end time means the restriction lasts until lifted. Expiry is evaluated at the time of the check; it does not require a separate expiry timer.

A silence suspends chat and multiplayer permissions. A full restriction also suspends spectating, score submission, beatmapset upload and screenshot upload. Restrictions do not remove granted permissions from the account; they change which permissions are effective for a period.

An area manager keeps the permissions of the area they manage while restricted. For example, an effective `RoomManageAny` holder keeps multiplayer permissions during a restriction. A manager of one area receives no exemption in other areas.

### Distinguishing denial outcomes

`UserSession.Check` distinguishes these permission states:

| `Access` result | Meaning |
|---|---|
| `Allowed` | The account has the required permission and it is effective now |
| `NotGranted` | The account was not granted every required permission |
| `Suspended` | The account was granted the permission, but an active restriction suspends it |

Scoped user operations report these outcomes as `UserResult.Ok`, `UserResult.NotAuthorized` or `UserResult.Restricted`. `NotAuthorized` also covers an actor who does not outrank the target. Other `UserResult` values describe operation-specific conditions, such as a protected account or invalid restriction.

### Hierarchy

Staff actions against another account—restricting it, changing permissions or password, revoking sessions, or deleting it—require the actor's **granted** permissions to be a strict superset of the target's granted permissions. Equal permission sets cannot act on each other. Granting permissions can give only permissions the actor holds.

The strict-superset rule does not apply to actions on oneself, except that nobody may restrict or lift a restriction on themselves. Action permission checks still use the actor's effective permissions.

### Limits that yield to higher rights

A limit one user sets on another does not bind an account with the matching higher permission. `ChatMessageAnyone` bypasses blocks and friends-only private-message settings. The same principle applies to room invitations with `RoomManageAny` and spectating with `RoomObserve`.

## Moderation history

Restrictions are records of the target, suspended permissions and time window. They do not contain a reason or note. Moderation actions append history with the action, actor, target, reason and timestamp; notes and login-history views are also recorded. This log explains past actions but never grants or removes a permission.

## Account identity and BasilBot

A name is unique among live accounts after normalization: matching is case-insensitive, and a space is equivalent to an underscore. Deletion is permanent; a deleted account is not restored, its id is not reused, and its name becomes available again.

BasilBot is the server's fixed user identity (id 1), has no permissions and cannot sign in. Its record cannot be deleted, restricted, given permissions or a password, or have its connections revoked; its public metadata may be edited.

**Planned:** BasilBot will be represented as a permanently online `UserSession` with a connection. Its announcements will be server-originated and will not pass through an ordinary user's permission check. A BasilBot command will act with the typist's scoped permissions, never with permissions belonging to BasilBot.

The operator console is also **planned**. It is the explicit operator entry point for creating the first administrator; no account receives administrator permissions implicitly.

## Invariants

* `Permissions` is the authority source; `Privileges` is derived and grants nothing.
* A permission check requires every requested bit and uses effective permissions.
* An active restriction suspends only its selected permissions and time window.
* A manager keeps permissions in the area they manage while restricted, not in unrelated areas.
* A permission that implies another includes its bits; callers check the smallest sufficient permission.
* Staff actions against another account require a strict superset of the target's granted permissions.
* A moderation log is history, not an authorization source.
* Names are unique among live accounts after normalization; deletion is permanent.
* BasilBot's commands use the typist's authority; its connected session and command integration are planned.

## Related code

* [`Permissions.cs`](../../src/Domain/Users/Permissions.cs): action permissions, `Allows`, effective permissions and derived roles
* [`PermissionsGroups.cs`](../../src/Domain/Users/PermissionsGroups.cs): permission masks, defaults and area managers
* [`Privileges.cs`](../../src/Domain/Users/Privileges.cs): roles understood by osu! and their direct permissions
* [`RestrictionRecord.cs`](../../src/Domain/Users/RestrictionRecord.cs): restriction target, permissions and time window
* [`UserSession.cs`](../../src/Application/Services/Contracts/Sessions/UserSession.cs): effective permission checks
* [`ScopedUser.cs`](../../src/Application/Services/Contracts/Users/ScopedUser.cs), [`ScopedUserRestrictions.cs`](../../src/Application/Services/Contracts/Users/ScopedUserRestrictions.cs): account actions and restrictions
* [`UserResult.cs`](../../src/Application/Services/Contracts/Users/UserResult.cs): account-operation outcomes
* [`RestrictionHandler.cs`](../../src/Infrastructure/Runtime/Sessions/RestrictionHandler.cs): infrastructure reaction to restriction events
* [`UserRecord.cs`](../../src/Domain/Users/UserRecord.cs): persistent account identity and normalized name

## See also

* [`multiplayer.md`](multiplayer.md): room authority and `RoomManageAny`
* [`chat.md`](chat.md): channel access, blocks and `ChatMessageAnyone`
* [`authentication.md`](../for-client/bancho/authentication.md): client authentication
* [`working-scopes.md`](working-scopes.md): supported and excluded account features
