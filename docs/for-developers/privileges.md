# Privileges

## Overview

An account's authority is its **privileges**: the flags enum `UserPrivileges` (`Player`, `Moderator`, `Supporter`,
`Owner`, `Developer`, `Tournament`). A privilege is stored on the account, granted by an owner, and is the only thing a
server action checks. There is no finer-grained right underneath it: an action is allowed because the actor holds the
privilege that owns it.

The values of the enum are the privilege bits the osu! (stable) client reports and receives, so the Domain type is also
the wire value. Nothing is derived, translated or configured in between.

## What each privilege allows

| Privilege | Allows |
|---|---|
| `Player` | Playing and chatting (public and private messages), creating and joining rooms, spectating, submitting scores, uploading beatmapsets and screenshots |
| `Supporter` | Downloading beatmapsets from the game client; osu!tourney also needs it |
| `Tournament` | Managing any room, viewing private matches, creating rooms past the creation limit, posting in any room's chat, watching rooms (osu!tourney), managing and viewing hidden beatmapsets |
| `Moderator` | Silencing accounts, adding notes, reading sessions and login history, messaging anyone, sending announcements, managing chat channels, managing screenshots |
| `Developer` | Changing the server settings and reading diagnostics |
| `Owner` | Managing accounts (create, delete, view deleted), setting privileges, managing server content |

A check asks for the privilege that owns the action: `Require(UserPrivileges.Moderator)`. A check requires every bit it
asks for, and an empty requirement is always met. A refusal reports the missing privileges, nothing more.

## Granting

* A new account holds `UserPrivilegesExtensions.Default` (`Player | Supporter`), or the server setting
  `accounts.defaultPrivileges` when the creator names none.
* `Tournament` always comes with `Supporter`: granting it adds the other bit.
* Setting an account's privileges needs `Owner`, and an actor can only give or take away privileges they hold
  themselves. A settings manager cannot make a privilege the default that they do not hold.
* Nothing implies another privilege. An owner who must act on every account is granted every privilege.
* The first administrator is made by the operator with a console command; nobody receives a privilege for being first.

## Hierarchy

A staff action aimed at another account (silencing, setting privileges or password, closing sessions, deleting it)
requires the actor's **granted** privileges to be a strict superset of the target's. Equals cannot act on each other.
Acting on oneself is exempt, except that nobody silences or unsilences themselves.

## Silence

A moderator can silence an account until a moment. The end time is stored on the account (`SilencedUntil`), so nothing
needs a timer: the silence is over when the clock reaches it. While it lasts:

* the account cannot post chat messages, public or private;
* other users cannot send it private messages, and the sender is told that the recipient is silenced;
* its privileges, rooms, scores and uploads are unaffected.

The osu! client is told how many seconds the silence lasts when it signs in and when it is silenced. Silencing and
lifting a silence are recorded in the moderation log. BasilBot cannot be silenced. A silence needs `Moderator`, and the
hierarchy rule applies.

## Limits that yield to higher rights

A limit one user sets on another (a block, friends-only messages) does not bind someone who holds the matching
privilege: `Moderator` bypasses blocks and friends-only private messages, and `Tournament` bypasses private-room
invitations.

## Moderation history

Moderation actions append history with the action, actor, target, reason and timestamp: silences, lifted silences,
notes, login-history views, session closures and password changes. The log explains past actions; it never grants or
removes a privilege.

## Account identity and BasilBot

A name is unique among live accounts after normalization: matching is case-insensitive, and a space is equivalent to an
underscore. Deletion is permanent; a deleted account is not restored, its id is not reused, and its name becomes
available again.

BasilBot is the server's fixed user (id 1), the voice the server speaks with, not an actor: nobody can sign in as it or
act for it, and its privileges only decide how a client shows it. Its record cannot be deleted, silenced, given
privileges or a password, or have its connections revoked; its public metadata may be edited. A BasilBot command acts
with the typist's privileges, never with the bot's.

## Invariants

* A server action is allowed by privileges and nothing else.
* A check requires every requested privilege.
* A silence stops chat only and ends by itself at its end time.
* Staff actions against another account require a strict superset of the target's granted privileges.
* A moderation log is history, not an authorization source.
* Names are unique among live accounts after normalization; deletion is permanent.

## Related code

* [`UserPrivileges.cs`](../../src/Domain/Users/UserPrivileges.cs): the privilege flags, `Allows`, the default set
* [`UserRecord.cs`](../../src/Domain/Users/UserRecord.cs): the account data, its privileges and `SilencedUntil`
* [`ScopedUser.cs`](../../src/Application/Users/Capabilities/ScopedUser.cs),
  [`ScopedUserSilence.cs`](../../src/Application/Users/Capabilities/ScopedUserSilence.cs),
  [`ScopedUserPrivileges.cs`](../../src/Application/Users/Capabilities/ScopedUserPrivileges.cs): account actions
* [`UserSilence.cs`](../../src/Application/Users/Objects/UserSilence.cs): the silence of an account
* [`UserOutcome.cs`](../../src/Application/Users/Objects/UserOutcome.cs): account-operation outcomes

## See also

* [`multiplayer.md`](multiplayer.md): room authority and the `Tournament` privilege
* [`chat.md`](chat.md): channel access, blocks and silences
* [`authentication.md`](../for-client/osu-stable/authentication.md): client authentication
* [`working-scopes.md`](working-scopes.md): supported and excluded account features
