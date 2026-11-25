# Identity, privileges and BasilBot rules

Privileges, silences, hierarchy, locks, sessions and BasilBot. The rules are binding: read this before
touching users, connections, privileges, silences or the bot.

* **Privileges are the only authority.** What an account may do is its `UserPrivileges` (Domain, on `UserData`): the
  flags Player, Moderator, Supporter, Owner, Developer and Tournament, whose values are the privilege bits the osu!
  client knows and are sent to it as they are. They are stored on the account and granted by an owner. There are no
  finer privileges beneath them and nothing is derived: a check asks for the privilege that owns the action. A new
  account holds `Player | Supporter`; granting `Tournament` also grants `Supporter`. Nothing implies another privilege,
  so an owner who must act on everyone is granted every privilege. Which privilege owns which action is the table in
  [`privileges.md`](../for-developers/privileges.md).
* **Room privileges are a separate concept.** `RoomPrivileges` (Domain) is the set of roles a user holds in one room (creator, referee, host, seated player). It is derived per room, never stored, and no account privilege is derived from it. A room scope only starts from the account's effective `UserPrivileges` (`Tournament` holds the creator role in every room, a missing `Player` holds no role). See [`multiplayer.md`](../for-developers/multiplayer.md).
* **Privilege checks require every bit** (`UserPrivileges.Allows`); an empty requirement is satisfied. A refusal is the
  outcome `Denied` and does not name the privileges.
* **A silence is a stored end time, not a restriction.** `UserData.SilencedEnd` holds the moment a silence ends; no
  timer is needed. A silenced account cannot post chat messages and receives no private messages (`Silenced`,
  `TargetSilenced`); its privileges, rooms, scores and uploads are unaffected. A moderator imposes and lifts it
  (`Moderator`); each is logged in the `ModerationLog`. There are no restrictions.
* **A block yields to a higher privilege.** A limit a user sets on others (a block, friends-only messages) does not bind
  someone who holds the matching privilege (`Moderator` for messages, `Tournament` for invitations and spectating). Every "prevent unless a higher privilege" rule follows this pattern.
* **Chat administration is a privilege, not an IRC mode.** Creating, configuring and deleting a general channel
  (through the API or `!channel`) needs `Moderator`; sending an announcement needs `Moderator`.
  Nobody manages a channel with `TOPIC`/`MODE`, and an IRC operator prefix grants nothing.
* **Moderation history is a log.** Silencing, lifting a silence, adding a note and viewing a user's sessions append a `ModerationLog` entry (action, `actor`, target, reason, timestamp). The log is for
  people; it never affects a privilege.
* **Hierarchy.** A staff action aimed at another user (silence, unsilence, set privileges, set password, revoke
  sessions) requires the actor's **granted** privileges to be a strict superset of the target's. Equals cannot act on
  each other; acting on oneself is exempt, except that nobody silences or unsilences themselves. Granting
  gives only privileges the actor holds.
* **Changing an account closes its sessions.** Deleting a user publishes `UserDeleted`; changing a user's privileges
  publishes `UserPrivilegesChanged`. The Sessions module reacts by closing all of that user's sessions, or only those the
  new privileges no longer allow. The effective privileges change at once, because a session reads the live `User`; the
  connection closes one event-queue step later.
* **BasilBot is the server's voice.** The server has one fixed user, id 1. At startup it makes sure id 1 is the bot: it
  takes over an existing id (restoring a deleted account under the default name, keeping a live one's name and country,
  setting its privileges to `Moderator`, removing its password and silence and closing its sessions) or creates it
  with the Vietnam flag. This is the one
  account the server creates and the one id it chooses. The bot is a friend of everybody. Nobody can sign in as it or
  act for it (no login, no presentable token, no delegation); its record is fixed (not deleted, silenced, given
  privileges or a password, or disconnected), while its metadata (name, country, avatar) is editable. It never passes a
  privilege check because it never asks for anything; its `Moderator` privilege is for display only (how a client shows
  it), and its own connections act with no privileges. It is a module (`Application.Bot` and
  `Infrastructure.Bot`) bound to user id 1. It is online with exactly one Stable and one Irc
  session, opened by the server with no transport behind them, and has no session type of its own: they are an
  ordinary `StableSession` and `IrcSession` that `Session.IsServerOwned` marks (the session of bot id 1).
  A server-owned session is not a game client (`UserSessions` does not count it as the user's stable client), is never indexed by token, never idle-closed
  and is not counted as a client in diagnostics, the console status or the dashboard; transports skip it when delivering. It answers chat commands. A command runs on the objects scoped to the
  **typist's** session and needs that user's privileges; the bot only replies. Its own messages and server announcements
  go straight to chat, past the privilege check. The wording lives in the replies file (see `basil-bot.md`).
* **A name is unique among live accounts.** Deleting an account is a soft, permanent delete: it is never restored and its
  id is never reused. Names are unique case-insensitively and with a space equal to an underscore
  (`UserData.NormalizedName`, plus a partial unique index); a new account may take a deleted account's name.
* **Identity on every action.** Every action has a real `actor` (a `Session`); `actor` is never `null`. A room
  created through the API has the caller as creator. A server announcement is `actor` BasilBot.
* **There is no delegation.** No account acts for another. An outside tool signs in with its own account and privileges.
* **Accounts come from registration, staff creation or the console.** Nobody is granted privileges for being first;
  a new account gets the defaults and more only by being granted. Anyone without an account may register one unless the
  `Accounts` lock is set. REST has no self-registration: `POST /v1/users` is for `Owner`. The server creates no
  account by itself; the first administrator is made by the operator with a console command.
* **The console is the operator's entry point.** It runs in `Basil.Host.Cli`, as the dashboard command box or as
  `Basil.Host <command>` from a shell. It acts through `SessionRegistry.OperateAsync`, which returns an `OperatorSession`:
  BasilBot's identity with every privilege, outranking every account, never registered, so no client can present it. It
  can also post chat messages as BasilBot (the dashboard's Chat tab): BasilBot reads a `!` command in such a message
  and runs it with the operator as the typist, so with every right. Only that session is read this way; what BasilBot
  itself says (a message with no actor) and what its own osu! and IRC sessions post are never parsed as commands, so
  no reply can start a command. It
  is the **one entry point that skips privilege checks**, because whoever sits at the console owns the machine; BasilBot
  itself acts with no privileges (its `Moderator` is display only).
* **Locks freeze an area for everyone but its manager.** `ServerSettings.Locks` is one flags enum: `Accounts`
  (registration; `Owner`), `Rooms` (creation; `Tournament`), `Beatmapsets` (upload and delete on every entry
  point; `Tournament`, while the file watcher keeps running) and `Profiles` (a user changing their own username,
  country, avatar or password; `Owner` may change another account's). A lock covers create, change and delete. It
  is checked in Application, never in a transport, and refuses with a `Locked` outcome.
* **Sessions and tokens.** One concept covers a sign-in, its token and its connection: the `Session`, one row in
  `sessions` with the auth data and the connection data together. There is no separate connection table, no connection
  history and no IRC key. Domain stores it as `SessionRecord` (the `Id` and the `SessionData`: the user, type, start, device (address and
  client), the SHA-256 hash of the token, the last heartbeat and the end), as `UserRecord` holds `UserData`; the Domain
  `Session` wraps one record with the rules of a single session (heartbeat, status, end). The Application `Session` (`StableSession`, `TourneySession`,
  `IrcSession`, `ApiSession`, `OperatorSession`) is the live actor (`IActor`) and holds it as `State`. The `Id` is a
  marker the API shows in place of the hash and opens nothing; the hash is the lookup key and is never shown. Several
  clients that share one API token are one session. Types: Stable, StableTourney, Irc, Api (the bot has none of its own,
  and the console's operator session is of type Api).

  Every heartbeat moves the last one on: every osu! request or ping (as bancho.py does), every IRC command including
  `PING` and `PONG`, every API request and every open SSE stream. The status comes from how long the session has been
  silent, with thresholds fixed in Domain (`SessionRules`, not settings): under 30 s it is `Active`; from 30 s it is
  `Replaceable`; from its `ExpiresAfter` (60 s for Stable and StableTourney, 300 s for Irc, 8 h for Api) it is `Expired`; once
  closed it is `Ended`. Expiry is lazy: no timer closes a session. It is decided when something looks at it: a
  heartbeat on an `Expired` session ends it `TimedOut` instead of reviving it, a lookup of an online user or a token
  closes it on the spot, and a new login applies the rule below. A background pass only flushes activity to the store
  and ends stored sessions that nobody resumed. The Domain `Session` owns these transitions and answers with a
  `SessionActionResult` (`Heartbeat`, `End`, `GiveWay`).

  Only an API session survives a restart: it resumes by its token while it is not `Expired`
  (`SessionRegistry.ResumeAsync`, one read of the store for a token the server does not hold). A closed or revoked session
  never resumes, and a resume checks the account again (deleted). osu! and IRC sessions are ended at startup with
  `ServerStopped`; an osu! client with an unknown token is told to restart and signs in again with its saved password,
  as bancho.py does, and IRC signs in with the account password in `PASS` (as osu! and the API do). IRC is plain TCP, so that password crosses the network readable; see
  [`https.md`](../for-technicians/https.md). Ended sessions are deleted after `Sessions.RetentionPeriod` (90 days by
  default). `Sessions.MaxPerUser` and `Sessions.ApiToken.Lifetime` no longer exist.
* **One active session per user and type.** Why: a second sign-in of the same kind is either a mistake (a client still
  running) or a takeover, and the silence of the old one tells which. The Sessions module enforces it in
  `SessionRegistry.Open`, under the account gate, for every type except StableTourney (no check, no count). It applies
  to the user's session in memory or, after a restart, only in the store. The decision when a new login finds an old
  session of the same type:

  ```text
  new login --> old session of this user and type?
                 |-- none ------------> open the new one
                 |-- Active ----------> refuse: AlreadyOnline
                 |-- Replaceable -----> end the old one (Replaced), open the new one
                 `-- Expired ---------> end the old one (TimedOut), open the new one
  ```

  A refusal is `LoginFailure.AlreadyOnline`: the osu! client gets bancho.py's "User already logged in." notice, IRC a
  `464` plus `ERROR`, the API a `409`. BasilBot's sessions are fixed at one Stable and one Irc. Refusals are counted in
  diagnostics (`basil.connections.rejected`, tags `type` and `reason=already_online`). Open live streams (SSE) have a
  per-user ceiling (`rateLimits.connections.streamsPerUser`) and answer `429`. This is a business rule of Sessions, not an admission
  policy of the gateway. An API session is no chat presence: it joins no channel, and an account that holds only API sessions is neither in `NAMES` nor in the osu! presence. In a channel, a new session of an exclusive type takes the place of the user's closed one of
  that type.
* **Logging out ends the session, not saved passwords.** Closing any session ends it and its token; the client can
  sign in again with its password. Changing a password ends every other session of the account. Changing the password
  only forces a new login: a client that stored the password can sign in again, so a real lockout of a stolen device
  needs a password change by someone else.
* **No authority by session type.** Never decide authority with `SessionType` or `is XSession`. The kind only
  decides what the client can technically do (an osu! client takes a slot; a tourney client gets no private-message
  channel). BasilBot's two sessions are never presented by a client, cannot be signed in as and are never
  closed by idle sweeps; transports skip them when delivering.
* **No third party in the core.** The server uses no third-party service.
  Country at registration comes from the host (the country header a trusted proxy forwards); Domain and
  Application receive the country as an input.
