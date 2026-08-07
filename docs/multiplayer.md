# Multiplayer & Tournament Match Report

## Overview

A multiplayer match in Basil is two things at once: a live room the bancho protocol drives in real time (see [`bancho.md`](bancho.md)), and a tournament record that outlives the room, the match report, or **TRT**. This page covers the room's lifecycle beyond creation, and how the TRT gets built.

## Why a report instead of just "the match"?

A tournament organizer needs to look back at a match after it's over: who played, which team won each round, individual scores. A live room doesn't outlive its players logging off, so that history has to be captured somewhere durable. Capturing it as its own always-up-to-date table would mean keeping a second copy of state in sync with the live room while the match is still running, duplicating state that's already tracked elsewhere.

## Contract

- **The report is never stored as a document.** `GET /matches/{matchId}` builds it at read time, every time, from the match's persisted rounds and scores, merged with the live room state if the match is currently in progress. There's nothing to go stale.
- **Every match sub-resource is a JSON base path plus a `.../live` SSE sibling**: hosts, referees, bans, slots, timer, and the room settings each get their own pair. See [`sse.md`](sse.md) for how the live half works; this page covers the plain-JSON contract.
- **`abort` and `close` are the two actions with no resource shape of their own**: plain `POST /matches/{matchId}/abort` and `.../close`, returning the resulting state instead of a bare success flag.
- **The winning team is computed at read time, not stored**: completed scores are grouped by team if the round used teams, otherwise the highest individual score wins.

## Concepts

- **A match ID is not a slot index.** `Matches.Id` is the stable, external id every API route and chat command uses. The in-memory 0-63 slot the bancho wire protocol itself assigns is a separate, short-lived number. See [`database.md`](database.md) for how the two relate in storage.
- **A round is created per beatmap played**, at `!mp start` / `MATCH_START`, not per match. A best-of-9 tournament match produces up to nine rounds, each with its own mode, win condition, mods, and scores.
- **`MatchSession.Lock`** guards every mutation to a live match's slots (see [`bancho.md`](bancho.md) for the concurrency reasoning). Every HTTP route that mutates match state acquires the same lock a packet handler would, so a `!mp` chat command and an admin API call can never race each other.
- **A room with zero seated players auto-closes after 5 minutes**, whether it was created by a game client, an IRC referee, or via the API — there's no separate "temporary" or "permanent" room concept. A 60-second warning is announced to the room and DM'd to any referee not already in it; a player joining after the warning cancels the close with its own announcement. See [`irc.md`](irc.md) for why an IRC-created or already-elsewhere-seated creator's room can start with nobody in it at all.
- **Referee, host, and creator are three separate roles, and only one of them is permanent.** Referee (`MatchSession.Referees`) grants `!mp` command authority and survives disconnects, logouts, and leaving the room — only `!mp removeref`/its HTTP equivalent revokes it. Host (`MatchSession.HostId`) is the opposite: transient in-client settings control, held only while physically seated, transferred by `!mp host` or lost by leaving. Creator (`MatchSession.CreatorId`) is set once, at room creation, to whoever ran `!mp make`/`!mp makeprivate` or created the room from the client — a room created via `POST /matches` has no creator at all. The creator counts as a referee unconditionally for as long as the room exists, can never be removed from the referee list (by name or by a full-list replace, on either the chat command or the HTTP API), and is the only one who can run `!mp addref`/`!mp removeref` from chat in the first place. A referee who loses that status while not currently seated is also removed from the room's chat channel — see [`irc.md`](irc.md) for the channel-standing rule this enforces.

## Lifecycle

```
!mp make / CREATE_MATCH
        |
match created; a game-client creator not already seated elsewhere is
placed in slot 0 and becomes host (see bancho.md and irc.md)
        |
!mp start -> a Round is created, CurrentRoundId recorded
        |
players play; scores submitted mid-round attach to CurrentRoundId
        |
MATCH_COMPLETE -> round marked ended (CurrentRoundId is left as-is)
        |
!mp start again -> a new Round begins, CurrentRoundId moves on
        |
        ...
        |
!mp close / POST /matches/{id}/close -> match ends
        |
GET /matches/{id} now builds its report purely from Rounds + Scores
```

## Design

**Why does `CurrentRoundId` survive past `MATCH_COMPLETE` instead of being cleared?** Score submission and the `MATCH_COMPLETE` packet arrive over two unrelated connections with no guaranteed order: the score is a multipart HTTP upload carrying a replay, `MATCH_COMPLETE` is a small packet on the persistent bancho connection, and in practice the packet routinely wins that race. If the round id were cleared as soon as the match completed, a score arriving a moment later would have nowhere correct to attach. Leaving it in place until the *next* `!mp start` means a late submission still lands on the round it was actually played in, with no race window to reason about.

**Why merge live state into the report instead of only reading from the database?** A tournament organizer checking a report mid-match wants to see what's happening now, not just what's already been recorded. Merging the live `MatchSession` in only when a match is still open gives both cases (finished and in-progress) the same endpoint, instead of a client needing to know which one to call.

## Related code

- `Basil.Application/Services/Multiplayer/MatchReportService.cs`
- `Basil.Application/Services/Multiplayer/MatchMembershipService.cs`
- `Basil.Web/Routing/Api/MatchRoutes.cs`
- `Basil.Web/Routing/Api/MatchSubResourceRoutes.cs`

## See also

- [`bancho.md`](bancho.md): the packet-level flow that creates and mutates a match
- [`sse.md`](sse.md): the live half of every match sub-resource
- [`database.md`](database.md): the `Matches`/`Rounds`/`Scores` tables the report reads from
- [`irc.md`](irc.md): why a room can start with nobody seated, and the empty-room auto-close timer
