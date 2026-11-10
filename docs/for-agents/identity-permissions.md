# Identity, permissions and BasilBot rules

Permissions, privileges, restrictions, hierarchy, locks, sessions and BasilBot. The rules are binding: read this before
touching users, connections, permissions, restrictions or the bot.

* **Permissions are the source of truth.** What an account may do is its `UserPermissions` (Domain, on `UserData`):
  one flat enum of independent actions named `[Resource][Manage]<Action>` (`RoomCreate`, `ChatPost`,
  `UserManageRestrict`, `ServerManageSettings`, …), split into `#region`s by resource. The enum has **no group
  members**. A manage permission contains the bits of the smaller permissions it implies (`BeatmapsetManage` holds
  `BeatmapsetManageViewHidden`, `RoomManageAny` holds `RoomManageViewPrivate`, `UserManageRestrict` holds
  `UserManageSilence`). Groupings live beside it in `UserPermissionsGroups`, one mask per area, each naming the
  permission that manages the area. A check asks for the smallest permission that allows the action; never
  `Allows(A) || Allows(B)` for a pair where one contains the other. Do not add a permission that is merely a
  consequence of another (playing and submitting scores are one: `ScoreSubmit`).
* **Privileges are roles, permissions are rights.** `UserPrivileges` is the flags enum osu! knows (Player, Moderator,
  Supporter, Owner, Developer, Tournament; the values are the osu! client's privilege bits, stored as they are and sent to the client as they are by `Host.Stable`). `privilege.Permissions` lists the permissions that
  belong directly to that role. A user has a role bit when their effective permissions hold at least one of those. A
  requirement written in privileges (a channel's `ReadPrivileges`/`WritePrivileges`) is met when every bit asked for is
  present. Privileges are derived, never stored or configured, and grant nothing.
* **Permission checks require every bit** (`UserPermissions.Allows`); an empty requirement is satisfied. Checks use the
  **effective** permissions: granted minus those suspended by active restrictions, computed at `now`.
* **Restrictions are timed records,** not account fields. A `UserRestriction` suspends a mask from `StartsAt` to `EndsAt`.
  Three operations, with different rights:
  * **Add** (`UserManageRestrict`): start a restriction for a mask; silence is one.
  * **Lift** (`UserManageRestrict`): set `EndsAt` to now; the record and its history stay. Like a pardon.
  * **Delete** (`UserManage`, higher): remove a record, including one still in force, from history; a `ModerationLog`
    entry is written. Like expunging a conviction.

  Named masks live on `UserRestriction`: `Silence = Chat | Multiplayer` and
  `Restrict = Silence | Spectating | ScoreSubmit | BeatmapsetUpload | ScreenshotUpload`. There is no free-text reason on
  the record. Expiry needs no timer. **Whoever manages an area cannot be restricted in that area:** the restriction is
  stored, but effective permissions keep every bit of the areas the user manages. A refusal reports only the missing
  permissions, not whether they were never granted or are suspended.
* **A block yields to a higher right.** A limit a user sets on others (a block, friends-only messages) does not bind
  someone who manages the matching area (`ChatManageMessageAnyone` for messages, `RoomManageAny` for invitations,
  `SpectatingWatch` for spectating). Every "prevent unless a higher right" rule follows this pattern.
* **Chat administration is a permission, not an IRC mode.** Creating, configuring and deleting a general channel
  (through the API or `!channel`) needs `ChatManageChannels`; sending an announcement needs `ChatManageAnnounce`.
  Nobody manages a channel with `TOPIC`/`MODE`, and an IRC operator prefix grants nothing.
* **Moderation history is a log.** Restricting, lifting, deleting a restriction, adding a note, changing permissions and
  viewing a user's sessions append a `ModerationLog` entry (action, `actor`, target, reason, timestamp). The log is for
  people; it never affects a right.
* **Hierarchy.** A staff action aimed at another user (restrict, lift, delete, set permissions, set password, revoke
  sessions) requires the actor's **granted** permissions to be a strict superset of the target's. Equals cannot act on
  each other; acting on oneself is exempt, except that nobody restricts or lifts a restriction of themselves. Granting
  gives only bits that are in effect for the actor.
* **BasilBot is the server's voice.** The server has one fixed user, id 1. At startup it makes sure id 1 is the bot: it
  takes over an existing id (restoring a deleted account under the default name, keeping a live one's name and country,
  removing its permissions, password and restrictions and closing its connections) or creates it. This is the one
  account the server creates and the one id it chooses. The bot is a friend of everybody. Nobody can sign in as it or
  act for it (no login, no presentable token, no delegation); its record is fixed (not deleted, restricted, given
  permissions or a password, or disconnected), while its metadata (name, country, avatar) is editable. It never passes a
  rights check because it never asks for anything; its privileges only decide how a client shows it. It is a module (`Application.Bot` and
  `Infrastructure.Bot`) bound to user id 1. It is online with exactly one Stable and one Irc
  connection, opened by the server with no transport behind them, and has no connection type of its own: they are an
  ordinary `StableConnection` and `IrcConnection` that `Connection.IsServerOwned` marks (the connection of bot id 1).
  A server-owned connection is not a game client (`User.Stable` stays `null`), is never indexed by token, never idle-closed
  and is not counted as a client in diagnostics, the console status or the dashboard; transports skip it when delivering. It answers chat commands. A command runs on the objects scoped to the
  **typist's** connection and needs that user's rights; the bot only replies. Its own messages and server announcements
  go straight to chat, past the rights check. The wording lives in the replies file (see `basil-bot.md`).
* **A name is unique among live accounts.** Deleting an account is a soft, permanent delete: it is never restored and its
  id is never reused. Names are unique case-insensitively and with a space equal to an underscore
  (`UserData.NormalizedName`, plus a partial unique index); a new account may take a deleted account's name.
* **Identity on every action.** Every action has a real `actor` (a `Connection`); `actor` is never `null`. A room
  created through the API has the caller as creator. A server announcement is `actor` BasilBot.
* **There is no delegation.** No account acts for another. An outside tool signs in with its own account and rights.
* **Accounts come from registration, staff creation or the console.** Nobody is granted permissions for being first;
  a new account gets the defaults and more only by being granted. Anyone without an account may register one unless the
  `Accounts` lock is set. REST has no self-registration: `POST /v1/users` is for `UserManage`. The server creates no
  account by itself; the first administrator is made by the operator with a console command.
* **The console is the operator's entry point.** It runs in `Basil.Host.Cli`, as the dashboard command box or as
  `Basil.Host <command>` from a shell. It acts through `UserRegistry.OperateAsync`, which returns an `OperatorConnection`:
  BasilBot's identity with every permission, outranking every account, never registered, so no client can present it. It
  is the **one entry point that skips rights checks**, because whoever sits at the console owns the machine; BasilBot
  itself still has no permissions. `--noconsole` turns the command box off.
* **Locks freeze an area for everyone but its manager.** `ServerSettings.Locks` is one flags enum: `Accounts`
  (registration; `UserManage`), `Rooms` (creation; `RoomManageAny`), `Beatmapsets` (upload and delete on every entry
  point; `BeatmapsetManage`, while the file watcher keeps running) and `Profiles` (a user changing their own username,
  country, avatar or password; `UserManage` may change another account's). A lock covers create, change and delete. It
  is checked in Application, never in a transport, and refuses with a `Locked` outcome.
* **Sessions and tokens.** A session is one sign-in, from its start to its end. A `SessionRecord` (a numeric `Id` with its `SessionData`: user, kind, start, device, the SHA-256 hash of the token, the expiry, the last activity and the end) is the persisted record of it. The `Id` is a marker the API shows in place of the hash and opens nothing; the hash is the lookup key and is never shown. Every session has a token, kept as a hash: the `cho-token` of an osu! session is stored like an API token or IRC key, but an osu! session lives only as long as its connection (it ends whenever the connection closes, idle included) and its token is never looked up again. Connection types: Stable, StableTourney, Irc, Api (the bot has none of its own, and the console's operator connection is of kind Api); a `Connection` is the live one and owns what changes while it runs (last activity, the sliding token expiry, the end); only a copy is written back. Api tokens and IRC keys are 256-bit secrets stored as SHA-256 hashes; the Api
  token expires after `Sessions.ApiToken.Lifetime` (sliding, one week by default) and is verified against the database
  the first time it is used, then kept in memory while its connection is used. An IRC key belongs to one device, is
  issued by `POST /v1/users/{id}/sessions/irc`, and is the only credential IRC accepts. osu! signs in with username and
  password every time. A connection that has sent nothing for its transport's idle timeout leaves memory; the session stays valid
  and the next use opens a new connection. Every connection leaves an entry in the session's trail (`connections`: address,
  opening, last activity, closing and why), written when the client connects and updated when it leaves; a client that
  comes back from the same address within five minutes of leaving continues its last entry instead of adding one. A
  start after the server stopped closes the entries left open at their last activity with `ServerStopped` and ends the
  osu! sessions the same way, as the room recovery ends the matches left running; API tokens and IRC keys stay valid.
  Reading the trail needs the same right as reading the sessions and is logged the same way. Ended sessions, with
  their trail, are deleted after `Sessions.RetentionPeriod` (90 days by
  default).
* **Concurrent sessions per user are limited per connection type.** The Users module enforces the limit in
  `UserRegistry.Open`, under the registry's lock, from the runtime setting `Sessions.MaxPerUser` (positive numbers;
  Rest `PATCH /v1/settings/sessions`, Cli `settings sessions set --maxPerUser.irc 7`). Defaults: Stable 1,
  StableTourney 10, Irc 5, Api 10; BasilBot's connections are fixed at one Stable and one Irc and ignore the setting. Only open
  connections count; at the ceiling, connections of that user and kind that have been idle beyond the idle timeout are
  closed first. Then Stable, StableTourney and Irc **refuse** the new connection (`LoginFailure.TooManySessions`: a
  Stable notice, an IRC `464` plus `ERROR`), while Api **closes the connection active longest ago** and accepts the new
  one (its token stays valid; the next use opens a connection again), so an Api login never fails on this rule. Open
  live streams (SSE) have a separate per-user ceiling equal to the Api limit and answer `429`. Refusals are counted
  in diagnostics (`basil.connections.rejected`, tags `type` and `reason=too_many_sessions`). This is a business rule
  of Users, not an admission policy of the gateway. Whether a new connection takes the place of the user's closed one in
  a channel is a different, fixed rule (`ReplacesOnJoin`: osu! game clients and BasilBot).
* **Logging out revokes credentials, not saved passwords.** Closing an Api session revokes its token; closing an osu! session only disconnects the
  client, which can sign in again with its password; closing an IRC session revokes that
  device's key. Changing a password revokes every other Api token and every IRC key, and closes osu! connections.
  Changing the password only forces a new login: a client that stored the password can sign in again, so a real lockout
  of a stolen device needs a password change by someone else or a restriction.
* **No authority by connection kind.** Never decide authority with `ConnectionType` or `is XConnection`. The kind only
  decides what the client can technically do (an osu! client takes a slot; a tourney client gets no private-message
  channel). BasilBot's two connections are never presented by a client, cannot be signed in as and are never
  closed by idle sweeps; transports skip them when delivering.
* **No third party in the core.** Apart from the optional beatmap mirror, the server uses no third-party service.
  Country at registration comes from the host (a trusted proxy header or an offline GeoLite2 file); Domain and
  Application receive the country as an input.
