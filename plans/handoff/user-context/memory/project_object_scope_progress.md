---
name: project-object-scope-progress
description: "State of the object-owned-behaviour redesign as of 2026-10-21: Application layering done (Services below Objects, AddApplication()); tests, Hosts, Cli left"
metadata:
  node_type: memory
  type: project
  originSessionId: 8cd03193-4520-4c7f-bd41-bf52f68a6113
  modified: 2026-10-20T11:47:45.265Z
---

Plans: `plans/object-scope-plan-20261016.md`, `plans/next-steps-20261021.md`, and the approved layering design `plans/application-layering-plan-20261021.md` (Option A: Domain <- Application.Storage <- Application.Services (internal, object-free) <- Application.Objects (public); Infrastructure.{Storage,Capabilities,Runtime}).

**Done and pushed on develop (8 projects build: Domain, Application.{Storage,Objects,Services}, Infrastructure.{Storage,Capabilities,Runtime}, Bot.Application; DI check 86 registrations; no tests run):**
Channels as Application objects (Domain keeps only GeneralChannelRecord/Data); UserSession merged into User (one live User per online period, transient offline instance never registered); IDE-suggestion cleanup pass + "Read the IDE" note in AGENTS.md; screenshots via ImageSharp port (IScreenshotInspector, 8192 px cap); server Notify/NotifyAll; audio preview probes with ffprobe (duration + bytes, no length cap); capability ports and Paging moved to Application.Storage; Infrastructure.Services renamed Capabilities; Infrastructure.Storage grouped Postgres/Files/Memory; UserRegistry owns online users, connections, login (AccountService object-free); Beatmapsets root + BeatmapsetIndexer; Lobby/Room/GeneralChannelRegistry took over room, match and channel services; score submission and anticheat report on BanchoConnection (ScoreRecorder object-free).

**Layering flip done 2026-10-21 (362a8246):** Services internal and below Objects (InternalsVisibleTo Objects), public `AddApplication()` in Objects, bridges removed, shared plain data in Application.Storage, Hosts csproj reference Objects only; docs/for-developers, AGENTS.md and plans updated.

**Left (ask the user before starting; he said Hosts/tests wait for his review):** migrate tests (ArchitectureTests rules: Infrastructure never references Services, Services has no public type), then Hosts, then Basil.Cli. Open questions in plans/next-steps-20261021.md section 3 (granted vs effective check when granting permissions, registries in Objects vs Storage, Silenced->Restricted, ConfigureAwait, line endings, split AGENTS.md, countdown-cancel reason, ModerationRecord link). Leftover locked worktree dirs to delete by hand: osuBasil-b2, osuBasil-wt-img, osuBasil-wt-store, osuBasil-wt-bm, osuBasil-wt-score.

**How to run agents:** spec in the scratchpad starting with "read tools-preamble.md"; `run-agent.sh` (`REPO=` env for a git worktree; Rider MCP only sees the main checkout, so worktree agents use shell/git mv and I run the DI check after merging); `opencode-go/gpt-6-luna`; at most two agents at once; review every diff line by line (agents add `.ConfigureAwait(false)`, reflow lines, turn switch statements into nullable-result switch expressions); stage only specific paths (`git add -A -- src ...`), never the whole tree. See [[feedback-review-and-format-after-agent]] and [[project-package-advisories]].
