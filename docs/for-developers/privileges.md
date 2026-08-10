# User privileges

## Overview

Basil represents account privileges as a bitfield: [`Basil.Domain.Users.Privileges`](../../src/Basil.Domain/Users/Privileges.cs).

A user can therefore hold any combination of privileges simultaneously. Basil does not model mutually exclusive roles
such as "moderator" or "tournament manager".

For example, a user who is both a moderator and a tournament manager has both `Moderator` and `TourneyManager` bits set
in the same `Privilege` value.

This representation also allows individual features to check only the capability they require without introducing a
hierarchy between unrelated privileges.

## Privilege flags

| Flag             | Bit | Value | Consumers                                 | Description                                                                                                               |
|------------------|----:|------:|-------------------------------------------|---------------------------------------------------------------------------------------------------------------------------|
| `Unrestricted`   |   0 |     1 | Login, match join, chat, score submission | Core gameplay privilege. Users without this flag are restricted and cannot play multiplayer, send chat, or submit scores. |
| `Verified`       |   1 |     2 | Login                                     | Automatically granted after a successful login. It does not independently gate a feature.                                 |
| `Whitelighted`   |   2 |     4 | None                                      | Legacy `bancho.py` compatibility flag. No consumer in Basil.                                                              |
| `Supporter`      |   4 |    16 | Login, `Donator`                          | Contributes to the client-facing supporter status.                                                                        |
| `Premium`        |   5 |    32 | `Donator`                                 | Legacy privilege with no direct consumer.                                                                                 |
| `Alumni`         |   7 |   128 | None                                      | Legacy `bancho.py` compatibility flag. No consumer in Basil.                                                              |
| `TourneyManager` |  10 |  1024 | `!mp`                                     | Allows tournament management commands in multiplayer matches.                                                             |
| `Nominator`      |  11 |  2048 | None                                      | Legacy `bancho.py` compatibility flag. No consumer in Basil.                                                              |
| `Moderator`      |  12 |  4096 | Channels, match join, IRC                 | Grants staff channel access, bypasses match password checks, and gives IRC operator status where applicable.              |
| `Administrator`  |  13 |  8192 | Channels, match join, IRC                 | Same authorization behavior as `Moderator` for these consumers.                                                           |
| `Developer`      |  14 | 16384 | Channels, match join, IRC                 | Same authorization behavior as `Moderator` for these consumers.                                                           |

The bit position is part of the compatibility contract because the resulting integer is persisted in the database and
mapped to client-facing privileges.

## Testing a privilege

Because privileges are independent bits, authorization checks should test the relevant bit rather than compare the
entire privilege value.

Use:

```csharp
if ((user.Privilege & Privileges.Unrestricted) != 0)
{
    // allowed
}
```

Do not use an equality check such as:

```csharp
if (user.Privilege == Privileges.Unrestricted)
{
    // ...
}
```

The equality form incorrectly rejects users who have the required privilege plus any unrelated privilege.

For a required combination, test the complete mask:

```csharp
const Privileges required = Privileges.Moderator | Privileges.TourneyManager;

if ((user.Privilege & required) == required)
{
    // both privileges are present
}
```

## Composite privileges

Some commonly used capabilities are represented as combinations of individual flags.

| Composite | Value | Composition                               |
|-----------|------:|-------------------------------------------|
| `Donator` |    48 | `Supporter \| Premium`                    |
| `Staff`   | 28672 | `Moderator \| Administrator \| Developer` |

These composites are convenience masks, not additional persisted privilege bits.

For example:

```csharp
Privileges.Donator
```

is equivalent to:

```csharp
Privileges.Supporter | Privileges.Premium
```

and:

```csharp
Privileges.Staff
```

is equivalent to:

```csharp
Privileges.Moderator |
Privileges.Administrator |
Privileges.Developer
```

This distinction matters when adding new privilege flags: a composite should only be changed when its intended
constituent capabilities change.

## Default privileges

New accounts receive the following privilege set by default:

```text
Unrestricted | Verified | Supporter
```

The resulting value is `19`.

This applies to accounts created through:

* in-game registration;
* `POST /users` on the `osu.` host with a matching `AdminKey`;
* `POST /users` on the `api.` host.

The admin API can explicitly provide a different `privilege` value.

In-game registration always uses the default privilege set.

## Verified auto-grant

`Verified` is also enforced during successful login.

If an account does not have the flag, [`LoginService`](../../src/Basil.Application/Services/Authentication/LoginService.cs) adds it:

```csharp
if ((user.Privilege & Privileges.Verified) == 0)
    newPrivilege = user.Privilege | Privileges.Verified;
```

This operation only adds `Verified`.

Login does not automatically grant:

* `Unrestricted`;
* `Supporter`;
* `TourneyManager`;
* `Moderator`;
* `Administrator`;
* `Developer`;
* any other privilege.

There is no special "first user" privilege escalation.

## Restricted accounts

`Unrestricted` is the core privilege for normal player activity.

An account without `Unrestricted` is considered restricted.

The restriction affects:

* multiplayer participation;
* chat;
* score submission.

Other privileges do not implicitly grant `Unrestricted`.

For example, a staff account without `Unrestricted` may still have staff-specific authorization while remaining
restricted from normal player actions.

Authorization code should therefore check `Unrestricted` explicitly when the operation requires unrestricted player
access rather than assuming that another privilege implies it.

## Staff privileges

`Moderator`, `Administrator`, and `Developer` form the `Staff` composite.

For the features currently implemented by Basil, these three flags have the same authorization behavior for:

* `#staff` channel access;
* match password bypass;
* IRC operator status.

They remain separate flags because the privilege model is also responsible for client-facing privilege mapping, where
they have different meanings.

A future feature may therefore distinguish these flags without changing the underlying representation.

## Tournament manager

`TourneyManager` is independent of `Staff`.

It grants access to tournament-management functionality exposed through `!mp` commands.

A tournament manager does not automatically become:

* a moderator;
* an administrator;
* a developer;
* a staff IRC operator.

Likewise, staff privileges do not automatically grant `TourneyManager`.

This separation is intentional: tournament control and server administration are different capabilities.

## Client-facing privileges

The server-side privilege model is not identical to the privilege names understood by the osu! client.

During Bancho login, Basil maps server privileges to protocol-level [`ClientPrivileges`](../../src/Basil.Domain/Users/Privileges.cs):

| Server privilege | Client privilege |
|------------------|------------------|
| `Unrestricted`   | `Player`         |
| `Donator`        | `Supporter`      |
| `Moderator`      | `Moderator`      |
| `Administrator`  | `Developer`      |
| `Developer`      | `Owner`          |

The mapping is intentionally asymmetric.

In particular, `Administrator` becomes the client's `Developer` privilege, while Basil's `Developer` becomes the
client's `Owner` privilege.

Code handling authorization should use `Privileges`, not `ClientPrivileges`. `ClientPrivileges` exists for protocol
compatibility and should not become the server's authorization model.

## Legacy flags

Several flags exist for compatibility with the original `bancho.py` privilege representation but currently have no
consumer in Basil:

* `Whitelighted`;
* `Alumni`;
* `Nominator`.

They should not be assigned new meaning merely because a bit is available.

If a new feature requires authorization, add or reuse an explicitly defined privilege with a clear consumer rather than
repurposing a legacy flag.

## Invariants

The privilege model depends on several rules:

* `Privilege` is a bitfield, not a single role.
* Individual authorization checks test bits with bitwise operations.
* Composite privileges are masks composed of individual flags.
* `Donator` is `Supporter | Premium`.
* `Staff` is `Moderator | Administrator | Developer`.
* `Unrestricted` is independent from staff and tournament-management privileges.
* Login only auto-grants `Verified`.
* There is no first-account privilege escalation.
* `ClientPrivileges` is a protocol representation, not the server's authorization model.
* Legacy unused privilege bits should not be repurposed without an explicit compatibility decision.

## Related code

* [`Basil.Domain/Users/Privileges.cs`](../../src/Basil.Domain/Users/Privileges.cs): privilege definitions and composite masks
* [`Basil.Application/Services/Authentication/LoginService.cs`](../../src/Basil.Application/Services/Authentication/LoginService.cs): `Verified` auto-grant and login privilege handling
* [`Basil.Application/Services/Multiplayer/MatchMembershipService.cs`](../../src/Basil.Application/Services/Multiplayer/MatchMembershipService.cs): privilege checks during match joining
* [`Basil.Application/Services/Chat/ChatDispatchService.cs`](../../src/Basil.Application/Services/Chat/ChatDispatchService.cs): unrestricted chat authorization
* [`Basil.Application/Services/Bot/MpCommandService.cs`](../../src/Basil.Application/Services/Bot/MpCommandService.cs): tournament-manager authorization
* [`Basil.Application/Services/Irc/IrcAuthenticationService.cs`](../../src/Basil.Application/Services/Irc/IrcAuthenticationService.cs): staff IRC behavior
* [`Basil.Web/`](../../src/Basil.Web/): HTTP-level administrative authorization

## See also

* [`authentication.md`](../for-client/bancho/authentication.md): client login and the `Verified` auto-grant
* [`deployment.md`](../for-technicians/deployment.md): creating accounts and granting staff privileges
* [`chat.md`](chat.md): chat authorization and command dispatch
* [`irc.md`](irc.md): IRC staff status and channel permissions
* [`multiplayer.md`](multiplayer.md): match membership and tournament permissions
