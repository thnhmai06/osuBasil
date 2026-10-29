# Identity, permissions and BasilBot rules

Permissions, privileges, restrictions, hierarchy, sessions and BasilBot. Moved out of `AGENTS.md`; the rules are binding. Read this before touching users, connections, permissions, restrictions or the bot.

* **Permissions are the source of truth.** What an account may do is its `UserPermissions` (Domain, on
  `UserData`): one flat enum of independent actions named `[Resource][Action]` (`RoomCreate`, `ChatPost`,
  `UserRestrict`, `ServerConfigure`, …), split into `#region`s by resource. The enum has **no group members**.
  A larger permission still contains the bits of the smaller ones it implies, because it is still *one
  permission* ("whoever holds this can also do what is below it"): a manage permission includes the matching view
  permission (`BeatmapsetManage` holds `BeatmapsetViewHidden`, `RoomManageAny` holds `RoomViewPrivate`,
  `UserRestrict` holds `UserSilence`). A group says only "these permissions belong together" and lives in the
  static class `UserPermissionsGroups` beside the enum, one mask per area (`Chat`, `Multiplayer`, `Spectating`,
  `Beatmapsets`, `Users`, `Server`), each with the permission that manages the area. The same pattern, `{Enum}Groups`,
  holds the combinations of every other flags enum (`GameModsGroups`, `BanchoFlagsGroups`); no flags enum has
  combination members. A check asks for the smallest permission that allows the action, so the manager and a user
  granted only that permission both pass; never `Allows(A) || Allows(B)` for a pair where one contains the other.
  Do not add a permission that is merely a consequence of another (playing and submitting scores are one:
  `ScoreSubmit`).
* **Privileges are roles, permissions are rights.** `UserPrivileges` (Domain; renamed from `BanchoPrivileges`) is one
  flags enum of the roles osu! knows (`Player`, `Moderator`, `Supporter`, `Owner`, `Developer`, `Tournament`; the
  values stay the wire bits). `privilege.Permissions` (an extension) lists the permissions that belong directly to
  that role, at depth one. A user has the bit of a role when their **effective** permissions hold at least one of
  those. A requirement written in privileges (a channel's `ReadPrivileges`/`WritePrivileges`) is met when the user
  has every bit asked for. Privileges are derived, never stored or configured, and they grant nothing: a client
  that claims a role it holds no permission for is refused.
* **Permission checks require every bit** (`UserPermissions.Allows`); an empty requirement is always
  satisfied. Checks use the **effective** permissions: the granted ones minus those suspended by the user's
  active restrictions (`Effective(restrictions, now)`).
* **Restrictions are timed records,** not account fields. A `UserRestriction` suspends a mask of permissions
  from `StartsAt` to `EndsAt` (open-ended until lifted; lifting sets `EndsAt`, history is kept). It holds only that
  data: no reason and no note. Named masks live on `UserRestriction`, not on `UserPermissions`:
  `Silence = Chat | Multiplayer` follows the osu! wiki (no chatting anywhere, no multiplayer; watching others play
  stays allowed) and `Restrict = Silence | Spectating | ScoreSubmit | BeatmapsetUpload | ScreenshotUpload`. There is
  one restrict operation, taking a mask. **Whoever manages an area cannot be restricted in that area:** the
  restriction is stored, but the effective permissions keep every bit of the areas the user manages (a
  `RoomManageAny` holder who is silenced still plays and manages multiplayer, but cannot chat unless they also
  manage chat). Expiry needs no timer: the effective permissions are computed at `now`. A refusal reports only the
  missing permissions (`Result<TOutcome>.Missing`), not whether they were never granted or are suspended.
* **A block yields to a higher right.** A limit a user sets on others (a block, friends-only messages) does not
  bind someone who manages the matching area (`ChatMessageAnyone` for messages, `RoomManageAny` for invitations,
  `RoomObserve` for spectating). Every "prevent, unless a higher right" rule follows this pattern.
* **Moderation history is a log.** Restricting, lifting, adding a note and viewing a user's login history append a
  `ModerationLog` entry (action, `by`, target, reason, `Timestamp`; stored through `IModerationLogRepository`). The log is for people to trace; it
  never affects any right.
* **Hierarchy.** A staff action aimed at another user (restrict, lift, set permissions, set password,
  revoke sessions, delete) requires the actor's **granted** permissions to be a strict superset of the target's.
  Equals cannot act on each other; acting on oneself is not subject to this rule, except that nobody restricts
  or lifts a restriction of themselves. Granting gives only bits that are **in effect** for the actor.
* **BasilBot is the server's voice.** The server has one fixed user, the bot, with id 1. Its name is not fixed (it can
  be renamed). At startup the server makes sure id 1 is the bot: if id 1 exists (even as another or a deleted account)
  the server takes it over (`UserRegistry.EnsureBotAsync`: it restores a deleted account under the default name `BasilBot`, keeps the name and country of a live one, and removes its permissions, its password and its restrictions, closing its connections); if it does not exist the server creates it at id 1. This is the one account the server creates by
  itself and the one id it chooses. The bot is a friend of everybody. It is not an actor: it is the voice the server speaks with, and the server protects
  it. Nobody can sign in as it or act for it, in any form (no login, no token a client can present, no
  delegation). It never goes through a rights check because it never asks for anything, so permissions mean
  nothing to it, and its privileges only decide how a client shows it; they grant nothing. Its record is fixed (it
  cannot be deleted, restricted, given permissions or a password, or have its connection revoked); its metadata
  (name, country, avatar) can be edited like any other user's. The bot answers chat commands (`!`, a fixed prefix) and
  posts the server's announcements. A chat command is an entry point like IRC or the API: it runs on the objects
  scoped to the connection of the user who typed it, so it needs that user's rights; the bot only replies. The
  bot's own messages go straight to the chat, past the rights check. The command handler and event-based
  announcers are implemented in `Basil.Bot`; the text of its replies is a JSON file the operator can edit (see
  `application-design.md`, "BasilBot replies"). The bot is online with a connection of its own (`BotConnection`,
  brought up by the server at startup; nobody can sign in as it) and a private message to it is a command. Showing the
  bot as online on the transports is planned for the Host phase. Countdowns are a feature of the room.
* **Identity on every action.** Every action has a real actor `by` (a `Connection`); `by` is never `null`.
  A room created through the API has the caller as creator. A server announcement is `by` BasilBot.
* **There is no delegation.** No account acts for another. An outside tool signs in with its own account and the
  rights that account holds.
* **Accounts come only from registration, creation or the console.** Nobody is granted permissions for being first
  or for anything else implicit; a new account gets the default permissions, and more only by being granted.
  Anyone without an account may register one unless `ServerSettings.LockedCreation` locks it. The server creates
  no account by itself: the first administrator is made by the operator with a console command (below).
* **The console is the operator's entry point.** *(Built in `Basil.Host.Cli`; the commands are specified in
  `docs/for-technicians/console.md`. It acts through `UserRegistry.OperateAsync`, which returns the `OperatorConnection`:
  BasilBot's identity with every permission and outranking every account. It is never registered, so no client can
  present it and BasilBot's own connection gains nothing from it.)* The server process offers an interactive console, like a Minecraft server, where the operator types
  commands. A console command acts as BasilBot (by its id, `BasilBot.Id`) and is **not subject to rights checks**: it is
  the one entry point that skips them, because whoever sits at the console owns the machine. Creating an account
  (and so the first administrator, with every permission) is a console command. The `--noconsole` flag turns the
  console off. The CLI is built on the `XenoAtom.*` libraries (not Spectre.Console). What the commands are and how
  they reach the objects is decided with the Host.
* **A name is unique among live accounts.** Deleting an account is a soft delete and is **permanent**: the account
  is never restored, and its id is never reused. The name is unique only among accounts that are not deleted, after
  normalization (case-insensitive; a space and an underscore are the same). `UserData.NormalizedName` does the
  normalization and the database enforces the same rule with a partial unique expression index. A new account may
  take the name of a deleted one.
* **No third party in the core.** Apart from the beatmap mirror, the server uses no third-party service for core
  work. Country at registration comes from the host (a proxy header or an offline GeoLite2 file); Domain and
  Application receive the `Country` as an input and do not know where it came from.
* **Creation locks.** `ServerSettings.LockedCreation` locks the creation of accounts, rooms and beatmapsets to
  their managers (`UserManage`, `RoomManageAny`, `BeatmapsetManage`), on every
  transport. Beatmapset uploads are locked by default. Importing a beatmapset has no actor in Application: the
  host gates it by policy (the lock, `BeatmapsetUpload`, `BeatmapsetManage`). There is no
  administrator key.
* **Sessions and tokens.** Every client signs in with credentials and gets a `Connection` with an opaque
  `Token` (in memory, lost on restart, no absolute expiry). Idle connections close after 300 s for osu!,
  osu!tourney and IRC and after 2 hours for `Api`. Logging out, changing the password, deleting the user, a
  permission change that removes access to a kind of client, and revoking close connections. osu! and IRC
  allow one connection per user; osu!tourney and `Api` allow several. `Api` connections do not make the user
  appear online in game.
* **No authority by connection kind.** Never decide authority with `ConnectionType` or `is XConnection`. The
  kind of client only decides what that client can technically do (only an osu! client takes a slot; an
  osu!tourney client gets no private-message channel). The kind `Bot` is BasilBot's own connection, opened by the
  server at startup so that the bot has a session and a private-message channel; no client can request it or
  sign in as the bot, no token of it can be presented, and idle sweeps never close it. Transports skip it when they
  deliver a message.
