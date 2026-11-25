# W14: every layer does only its own job

Source: the layer audit `plans/review/layer-audit.md` (55 findings, ids below), checked against the roles the owner fixed
on 2026-11-25 (`docs/for-developers/architecture.md` "Layers", `AGENTS.md` "Hosts, Gateway and Server"):

| Layer | Its only job |
|---|---|
| Gateway | Guard: block and rate-limit each request; may use Application services and capabilities to guard per object (user, session, room). Never processes a request. |
| Host | Translate the wire into the system's messages and route them; translate the system's language back out. No system logic. |
| Protocol | The languages the outside world lacks (osu!, IRC). |
| Application | Process requests through its contracts. A `Scoped*` capability is the actor reaching a feature of an object: who-may decisions live in it. |
| Infrastructure | Third-party services for Application. |
| Domain | The core business and its rules. |

Already clean: Gateway (it only lacks the work it should take over), both Protocol projects, Domain.

## Decisions (owner, 2026-11-25)

* All three phases (A, B, C) before 1.0.
* `Logic/` leaves Infrastructure: rules go to Domain, object-free coordination to `Basil.Application.<Module>/Logic/`;
  Infrastructure keeps repositories, storages, adapters and workers. `AGENTS.md`, `module-layout.md` and
  `architecture.md` change in the same phase.
* I3, I4: IRC keeps refusing `#spec_` and KICK/INVITE/TOPIC/MODE as a documented non-feature in the IRC guide; no new
  messages.
* Wire limits (X9, S12, S13) stay in the hosts as translation, documented as such.
* A1 is out of W14: D16 keeps the session kinds.
* I5 and the IRC half of X5 are done in W14 (owner, 2026-11-26): Chat publishes the audience when a user's last
  connection leaves; IRC drops its membership copy.
* Before W14 closes: the load harness (baseline `4dc697312` against W14) and the container check.
* Verification: a Release build and the load harness after every phase (agents still build only and run no tests).

The original questions, kept for the record:

1. **Scope before 1.0.** All three phases, or A and B before 1.0 and C after. Recommendation: A and B before 1.0, because
   they are rules decided in the wrong place (privileges, authority, state); C is placement that does not change behaviour.
2. **`Logic/` in Infrastructure (N3, N4).** `AGENTS.md` today puts object-free application logic (account checks, score
   validation, beatmapset indexing) in `Basil.Infrastructure.<Module>/Logic/`. The new definition of Infrastructure
   ("third-party services") excludes it. Options: move that logic to Application (`Logic/` beside `Objects/`), or keep the
   sanctioned exception. Recommendation: move it to Application; Infrastructure keeps repositories, storages, adapters and
   workers.
3. **Wire limits in hosts (X9, S12, S13).** Cutting chat to a wire's line length (osu! 450, IRC 512 bytes), the outbox
   back-pressure that drops a stuck client, and the per-request id caps are properties of each wire, not system rules.
   Recommendation: keep them in the hosts as translation, written down as such; the system's own message limit stays in
   Domain (`Message.MaxLength`).

## Phase A: guarding moves to the Gateway

The Gateway may now use Application services, so it takes everything that decides whether a request may come in.

| Id | Today | After |
|---|---|---|
| HS1 | `Host.Shared` `AdmissionRefusals.SubjectOf` gives `Tournament` the elevated tier | The Gateway reads the session through Application and picks the tier; hosts pass the session |
| X3 | `StableAdmission` looks up the token and switches to the `Poll` class | The Gateway classifies from the session it resolves |
| X1 | Four hosts reserve login attempts in `LoginAttemptGuard` and decide on failures | One Gateway call per sign-in (`AdmitSignIn` / `EndSignIn(failed)`); hosts only translate the answer |
| X2 | Hosts apply `RateRule.Chat`, `RestMutation`, `GcCollect`, `Registration` themselves | Each entry point declares its rule; the Gateway applies it |
| S5 (gate half) | "An open osu! client at this address" gate before reading an upload, in `WebAuthentication` | A Gateway check for the Stable web uploads |
| — | Per-user stream cap (`connections.streamsPerUser`) in the SSE hub | Gateway, as a per-object concurrency allowance |

Done when: no host calls `RateLimiter`, `LoginAttemptGuard` or reads privileges to decide admission; `Host.Shared` holds
no admission rule; the denial counter still counts every refusal.

## Phase B: rules leave the hosts for Application and Domain

Each host change is the same move: the message carries the ids the request names, the handler scopes on that object,
the capability returns the outcome, and the host only translates the outcome.

**Privileges and authority (high)**

| Id | Today | After |
|---|---|---|
| R1 | `UserRoutes.RelationAsync` answers 403 `notSelf` when the path id is not the caller | `AddFriend(Actor, Id, OtherId)` and the three others; `ScopeTo(actor, id).Relationships.AddFriendAsync(otherId)` answers `NotSelf` |
| S1 | `StableView.Roles` turns no privilege into `Player`, and `StableLogin.ReadableBy` uses it to pick readable channels | The channel list at login comes from Chat (`ListJoinableChannels`); `Roles` stays a wire-only mapping |
| S2 | `OsuWebRoutes` refuses osu!direct with `MayUseDirect()` | The direct search and download messages answer `Denied` |
| I1, I2 | IRC computes `@`/`+` and the visible channels from privileges | Chat returns the member's role in a channel and the channels a session may see |
| I3, I4 | IRC refuses `#spec_` and KICK/INVITE/TOPIC/MODE on its own | Messages whose outcomes say what is allowed; the host maps them to numerics |
| S6 | Stable ignores a packet unless the session is a tourney session | Left to `ScopedRoomMembers`, which already checks it |
| HS6 | The room command list restates who may run each command | Generated from `RoomPrivileges`, or dropped |

**State and coordination (high and medium)**

| Id | Today | After |
|---|---|---|
| R2 | The spectate stream refuses an offline target and undoes the spectate itself | `StartSpectating` answers the refusal; the cleanup lives in Application |
| S4 | Stable decides who receives relayed scores and spectator frames and sends them | A message per score/frame; Application picks the recipients and publishes |
| S3, A2 | Registration calls `AccountRegistration.CheckAsync` directly, then rate-limits, then registers | `CheckRegistration` and `RegisterAccount` messages; the rate rule goes to the Gateway (phase A); `CheckAsync` becomes internal |
| S5 (auth half) | `WebAuthentication` finds the user, keeps a digest cache and verifies | A `SignInWeb` message to Sessions |
| S9 | Stable validates the token and detects expiry itself | `ResumeSession`, as REST does |
| S7 | Create-match is two commands; settings changes are diffed in the host | One message with the full settings; Application diffs |
| S8 | Stable calls `IAnticheatReporter` and answers `-3` from its flags | The flags go into `SubmitScore`; the outcome says what to answer |
| X4 | Hosts find recipients by scanning online users | Messages that take a name and resolve it in Sessions/Chat |
| X5 | Delivery decides who sees private rooms, `NoMap` players, away notices | Events carry their audience; delivery only writes |
| X6 | A user PATCH becomes up to five commands run in order | One `UpdateUser` message, applied by the user object |
| X7 | Announce resolves recipients twice | `NotifyUsers` takes ids and answers which are missing |
| X8, HS5 | Hosts choose defaults (new channel, countdown 30 s, no mods) | Defaults on the Domain objects and messages |
| HS3 | `UserView.Of` derives effective privileges and silence from the live user | The query returns them; the view only copies |
| HS4 | Settings patches are merged in the host | `UpdateSettings(category, patch)` merged in Application |
| R3 | The room live stream polls and diffs the room | Room events feed the SSE hub |
| R5, C2 | Routes and the dashboard read `Lobby`, registries and `SessionRegistry` directly | Query messages |
| I5 | IRC keeps its own copy of channel membership | Events carry `FirstOfUser` |
| S11 | "Accepting logins" lives in the Stable and IRC hosts | One server state in Application |
| S10 | Unknown grade or mode in a score becomes a default | Refused through `WireMap.Known` |
| C1, C3, R4, R6, R7 | `--admin` privilege set, screenshot paging, 10 MB banner limit, pick-by-file-name, default avatar | Each to its query, setting or object |

## Phase C: rules settle in Domain and Application

| Id | Today | After |
|---|---|---|
| HS2 | Room name cut to 50 characters in four places, not in Domain; the osu! path never cuts | `RoomSettings` owns the limit (refuses or cuts the same way for every path) |
| N1 | `BeatmapVisibility` (who sees hidden beatmaps) in Infrastructure, used by six handlers | A capability on beatmapsets in Application |
| N2 | Chat handlers decide who leaves a room channel | The channel object decides on the room event |
| N3, N4 | Account and score rules in `Infrastructure/Logic/` | Per decision 2 |
| N5 | Countdown limits and refusals in a worker | `RoomCountdown` in Application |
| N6, N7 | Leaderboard size and composition in handlers; score routed by scanning rooms | Query objects; `Lobby.ReceiveScore` |
| A1 | Chat and Multiplayer decide by connection kind (`is ApiSession`, `StableSession`) | Accepted under D16 today; revisit with the identity rules |
| B1 | The update check calls GitHub from the composition root | An Infrastructure service behind an Application contract |
| B2, B3 | Shutdown closes rooms itself; `ShellRunner` prompts and names types by string | `Server.ShutdownAsync`; prompt to `Host.Cli`; `nameof` |

## How it is carried out

One Sonnet agent per phase, in order A, B, C, on `develop`, as in W1 to W13: Rider MCP for moves and renames, OpenCode Go
for mechanical edits, build only. Phase B is split in two dispatches (privileges and authority first, then state and
coordination). The coordinator reviews each diff against the roles above and `AGENTS.md` "Final verification", builds
Release, runs the compose stack and the load harness (`PERF.md` scenario) to catch regressions, and commits.

Risk: tests are deferred, so these moves are checked only by build, review, a smoke run and the load harness. The
sign-in, spectating, IRC membership and room flows change shape; a real-client session (E9) after phase B is the
strongest check.

## After W14

The rest of the road to 1.0 is unchanged: load test to the ceiling, real clients behind nginx (LAN and Cloudflare),
tests (deferred), then `CHANGELOG.md` `[1.0.0]`, merge to `main` and tag `v1.0.0`.

## Estimate

About six to seven Sonnet sessions, sized on W10 to W13 (each 7 to 18 minutes, 185k to 390k tokens): phase A one, B1
(privileges and authority) one, B2 (state and coordination, about twenty items) two, C one, and one or two for fixes
after review. A and B alone before 1.0: four to five. Gathering A with B1, or leaving the light B2 items (C1 to C3,
R4, R6, R7, S10, S11) after 1.0, saves one session each.
