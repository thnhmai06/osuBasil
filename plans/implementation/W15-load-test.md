# W15: load test of a live tournament

Group assignment on non-functional testing: prove **one** non-functional requirement, load capacity. The method follows
the course's test plan (six test kinds: load, stress, spike, soak, capacity, scalability); the figures in its examples
only illustrate what to design and are not targets. The lecturer's rule: the computing infrastructure and the bandwidth
are fixed, so the software is the only variable, and the software must be described fully enough for the simulation to
cover all of it.

Deadline: the infrastructure and scenarios on 2026-11-25; runs and raw reports on 2026-11-26 and 2026-11-27. The owner
writes the report and attaches the raw NBomber reports and the server metrics as evidence.

## Decided with the owner (2026-11-25)

**Claim.** "Basil serves N concurrent online sessions during a live tournament and still meets its SLO." N is set after
the pilot, at about 70–80 % of the measured ceiling. N counts sessions, not people.

**Fixed infrastructure.** Every test flow uses the LAN address of mocha (192.168.27.124), never Tailscale.

| Node | Hardware | Link to mocha | Role |
|---|---|---|---|
| mocha | i7-11370H 8c, 16 GB | wired 1 Gb/s | the whole stack (nginx, Basil, Postgres), pinned cores |
| cappuccino (192.168.27.151) | 4c, 3.4 GB | Wi‑Fi, RTT 2.7–23 ms, mean 8.5 ms | NBomber generator (requests 3 CPU) |
| espresso (192.168.27.150) | 4c, 3.4 GB | Wi‑Fi, RTT 1.5–20 ms, mean 8.1 ms | control load, ping and iperf3 during every run |

**Workload.** The unit is a tournament room:

* 2–8 players (1v1, 2v2, 4v4);
* one referee, half over IRC `!mp`, half over REST;
* 0–2 osu!tourney clusters, each one manager plus one child client spectating each slot;
* a few in-game spectators;
* overlay viewers on SSE `/matches/{id}` and `/rooms/{id}/live`.

Outside the rooms:

* free players and casual rooms;
* a background layer that covers every basic activity, each at a low but real rate: presence, friends, PMs, channels,
  search, `/d/` (no-video set), getscores, score submission with replay, getreplay, difficulty-rating, avatar, cover,
  thumbnail and preview reads, avatar upload, screenshots, seasonal, BasilBot `!roll/!help/!where/!faq`, IRC
  NAMES/WHO/WHOIS/LIST/TOPIC/AWAY, REST reads;
* login/logout churn;
* deliberate edge cases that the rules refuse.

Data: the 15 real beatmapsets supplied by the owner.

**SLO.** Measured on the server; client end-to-end latency and the network RTT are reported beside it. Basil's
diagnostics break HTTP latency down by host only (`c.` carries login and poll, `osu.` carries getscores, submission,
getreplay and search), so the per-channel server-side source is nginx on mocha: a load `log_format` with
`$upstream_response_time`, `$request_time`, `$status`, the URI and whether an `osu-token` header was sent (poll versus
login). Upstream time excludes the Wi‑Fi. The error rate is computed from nginx status codes; client timeouts are
reported separately next to espresso's control trace. IRC has no server-side measure and is judged client-side,
labelled as such.

| Channel | Target |
|---|---|
| osu! poll | p95 ≤ 200 ms, p99 ≤ 500 ms |
| score submission | p95 ≤ 1 s |
| getscores | p95 ≤ 500 ms |
| REST read | p95 ≤ 300 ms |
| login | p95 ≤ 2 s |
| real errors | < 0.5 % |
| server | CPU ≤ 80 %, no OOM, no restart |

`/d/` downloads and uploads are reported separately and are not judged, because Wi‑Fi dominates them.

**Outcome classes.** Only `error` counts against the SLO.

| Class | Meaning |
|---|---|
| `ok` | succeeded |
| `refused` | a business rule refused it, as expected |
| `throttled` | 429 |
| `shed` | 503 |
| `error` | 5xx, timeout, protocol mismatch, IRC or SSE dropped |

**Tests.** Pilot, then Load ×3, Capacity, Stress (recovery measured), Soak 6 h at 70 % of N, Spike ×2
(every room starts at once), Scalability (Basil pinned to 1/2/4 cores).

* Never cut: Load, Capacity, Stress, Soak.
* If late: Scalability drops to two core counts, then Spike drops to one run.

**Out of scope:** NBomber Cluster, unit and mutation tests, tuning Basil this week.

## Design rules

1. **The software's own limits stay at their defaults.** The gateway (per user, concurrency, shedding), the chat limit
   and the BasilBot queue are behaviour under test. Only nginx's per-address limits are raised: every virtual user comes
   from one address, which stands in for many real ones. One temporary exception: REST mutations are raised while the
   accounts are created, and put back before any measured run.
2. **A virtual user obeys the rules as a real client does.** Each step is a legal transition from the current state,
   taken after reading the server's real answer. A shared **World** keeps rooms, players in game and online users, so
   every target exists. Randomness has a fixed seed, written into the report.
3. **One identity per virtual user.** The fingerprint, version and uninstall/disk hashes given at login match the score
   submission, because the server checks them.
4. **Every number has a source.**
   * client: NBomber;
   * server: Basil's diagnostics SSE;
   * infrastructure: docker stats, `pg_stat_activity`, CPU temperature and clock, ping and iperf3.

## Structure

The projects are flat at the repository root, each in a folder of its own name (no `tools/` or `tests/` parent), listed
in `Basil.slnx` under a `/Tests/` solution folder. What every test can share lives in `Basil.Tests.Shared.*`;
`Basil.Tests.LoadTests` holds only the six load tests, each a module of its own. Inside a project the code is split by
folder, namespace = project + folder. Deployment resources (k3s manifests, nginx configuration, run script) live apart from the
code, in `docker/loadtests/`.

| Project | Holds | References |
|---|---|---|
| `Basil.Tests.Shared.Clients` | the simulated clients, speaking the wire like real ones: `Stable/` (login, poll, packets in and out), `OsuWeb/` (getscores, submission with the score cipher, getreplay, search, `/d/`, screenshot, getfriends, seasonal, difficulty-rating), `Irc/`, `Rest/` (with an SSE reader) | `Basil.Protocol.Game.Stable`, `Basil.Protocol.Irc` |
| `Basil.Tests.Shared.Workload` | what the simulated people do: `World/` (rooms, players, online users, beatmaps, identities), `Personas/` (MatchRoom, FreePlayer, CasualRoom, Spectator, TourneyCluster, OverlayViewer, IrcChatter, Background, Churn, EdgeCases), `Outcomes/` (ok, refused, throttled, shed, error) | Clients, NBomber |
| `Basil.Tests.Shared.Environment` | the test bench around the server: `Seed/` (accounts, beatmap import wait, `world-data.json`), `Monitor/` (diagnostics SSE into report metrics; docker stats, `pg_stat_activity`, CPU temperature and clock, ping and iperf3) | Clients, NBomber |
| `Basil.Tests.LoadTests` | the six tests and the entry point that picks one | Workload, Environment, NBomber |

```
Basil.Tests.LoadTests/
  Load/          closed model: warm-up, ramp, steady N, ramp-down; SLO thresholds          (run ×3)
  Stress/        steps 1x to 2x the ceiling, then back to 0.5x; recovery time
  Spike/         background load, every room starts at one instant, back to background
  Soak/          70 % of N for 6 h; per-hour windows
  Capacity/      steps of +ΔN every 5 min until the SLO breaks; the pilot is a short Capacity run
  Scalability/   Capacity repeated with Basil pinned to 1, 2 and 4 cores; scaling efficiency
  Program.cs     `Basil.Tests.LoadTests <test> [options]`, one test per run, one report folder per run
docker/loadtests/  k3s/ (manifests), nginx-load.conf, run.sh (deploy, run, collect)
```

Each test module only declares its load shape, its thresholds, what it measures and its report name. The modules share
one small contract (a test has a name, builds its scenarios from the Workload and the Environment, and declares its
load simulations and thresholds), so adding or changing one never touches another.

**Place in the architecture.** All four projects are an outside client of the system, a black box like the osu!
client, an IRC client or a tournament tool:

* The only Basil projects they reference are `Basil.Protocol.Game.Stable` and `Basil.Protocol.Irc`, the languages a
  client speaks, through `Basil.Tests.Shared.Clients`. None references Domain, Application, Infrastructure, a Host, the
  Gateway or the composition root `Basil`, and no production project references them.
* They reach the server only over the network (osu!, osu-web, IRC, REST, SSE), through nginx, the way real clients do.
  Every fact they need (beatmaps, rooms, scores, settings) comes from the public API, never from the database or
  `Data/`.
* `Basil.Tests.LoadTests` is an executable run by NBomber, not an xUnit project, so `dotnet test` never runs it.
  `Basil.Tests.Shared.Clients` carries no NBomber dependency, so the host and end-to-end tests written later can use
  the same simulated clients (`testing.md` names them as shared support).
* The docs that list the projects and the test layers (AGENTS.md, `architecture.md`, `testing.md`) are updated to say
  this.

Other design points:

* The wire code moves from `tools/LoadHarness`. LoadHarness is then removed with its `/Tools/` entry, and PERF.md points
  to the new project, so there is one harness.
* Each persona is a scenario. A copy is a room (MatchRoom runs the whole room, one task per session) or a session
  (single personas). Each kind of request is its own step (`poll`, `submit`, `getscores`, `login`, `rest`, `irc`,
  `sse`, `download`, `upload`), so the report has p50/p95/p99 per channel. The SLO is declared as thresholds.
* Session personas use the closed model; anonymous requests use the open model (Inject).
* Spike uses a shared tournament clock: every room's referee waits for the same instant, then runs `!mp start`.

## Infrastructure

Every run goes through k3s: the server stack, the generator and the control load are Kubernetes workloads pinned to
their nodes, and the manifests are the written record of the fixed infrastructure (node, CPUs, memory, ports, images).
SSH to the workers only places the published binary on the node and serves system checks.

**Cluster facts (2026-11-25).** k3s v1.37.2; mocha is the control plane, cappuccino and espresso are workers; the
default storage class is `local-path`. Flannel runs over the Tailscale addresses (100.100.x), so pod traffic between
nodes would go through WireGuard; traefik's service load balancer holds 80 and 443 on every node.

**Placement** (`docker/loadtests/k3s/`, namespace `basil-load`):

| Workload | Node | Network | Resources |
|---|---|---|---|
| `postgres` StatefulSet, `local-path` volume | mocha | cluster | Guaranteed, 1 CPU |
| `basil` Deployment (1 replica), `Data/` as a hostPath of the prepared data folder | mocha | cluster | Guaranteed, 4 CPU (1 and 2 for Scalability) |
| `nginx` Deployment, `nginx-load.conf` as a ConfigMap | mocha | `hostNetwork`, ports 8443 (HTTPS) and 6697 (IRC TLS) | Guaranteed, 1 CPU |
| `loadtests` Job, one test per Job | cappuccino | `hostNetwork`, `--connect 192.168.27.124 --port 8443` | 3 CPU (4 allocatable, svclb takes a share) |
| `control` Job (control load, ping, iperf3) | espresso | `hostNetwork` | 1 CPU |
| `iperf3` server | mocha | `hostNetwork` | — |

* `hostNetwork` on the generator, the control and nginx keeps every measured hop on the LAN (192.168.27.x) instead of
  the Tailscale overlay; nginx reaches Basil inside mocha. nginx stays off 443 because traefik holds it.
* Guaranteed pods with whole CPUs get exclusive cores once mocha's kubelet runs the static CPU manager, which is the
  Kubernetes form of the core pinning. Basil 4 + Postgres 1 + nginx 1 leaves 2 shared cores (one reserved) for the
  desktop, the k3s control plane, containerd and the monitor. Scalability changes only Basil's CPU count.
* nginx does all the TLS on one core and may reach its ceiling before Basil; its CPU is watched in the pilot so the
  Capacity bottleneck is attributed correctly.
* Basil runs headless in the pod (compose gives it a tty); `terminationGracePeriodSeconds` is 90, as compose's stop
  grace period.
* `Basil:Server:TrustedProxies` names the address Basil actually sees nginx come from (likely mocha's `cni0`, checked in
  T1), so the forwarded address and the PROXY header are believed; T1 proves one IRC registration and one forwarded
  address.
* Logging is set to Information through the Deployment's environment.

**Images**

* Basil: `docker build` on mocha, then imported into k3s's containerd on mocha (`k3s ctr images import`, root), with
  `imagePullPolicy: Never`.
* The generator and the control load: the public `mcr.microsoft.com/dotnet/runtime-deps` image with the self-contained
  publish mounted from a hostPath (`~/loadtests/bin` on the node, copied there with `scp`). The workers need no
  registry, no root and no restart.

**Data and reports**

* `/home/haith/Desktop/osuBasil Data` is prepared once on mocha:
  * `Mapsets/*` to `Beatmapsets/`;
  * `Seasonals/` to `Menu/Seasonals/`;
  * `Faqs/` to `Faqs/`.
* The osu! calculation stack is installed into `Data/Rulesets/` (`ruleset update`, an internet download); without it pp
  is off, the score wait never runs and the heavy maps stop mattering.
* **Reset between runs.** After seeding, `pg_dump` the database. Before every measured run: restore the dump, clear
  `Replays/`, `Screenshots/`, uploaded avatars and `Logs/`, restart the Basil pod. Every run starts from the same state.
* Each Job writes its report folder to a hostPath on its node. `run.sh` waits for the Job, copies the folder back to
  `reports/<test>-<timestamp>/` on mocha, and adds the infrastructure record (manifests, `kubectl top`, node CPU
  temperature and clock).

**Owner actions (root on mocha, once)**

* Enable the static CPU manager on mocha's kubelet: `--kubelet-arg=cpu-manager-policy=static` and
  `--kubelet-arg=reserved-cpus=0`, delete `/var/lib/rancher/k3s/agent/kubelet/cpu_manager_state` (kubelet refuses to
  start after a policy change otherwise), then restart k3s.
* Import the Basil image (`sudo k3s ctr images import`), again after each rebuild.
* Set the CPU governor to `performance`.

## Tasks

Order: T0 ‖ T1, then T2 ‖ T3, T4, T5, then T6 ‖ T7, then T8, built as a **vertical slice first**: Clients, then
MatchRoom (players and one IRC referee), FreePlayer and Spectator, then the Load module and a smoke run. Tourney,
Overlay, Background, Churn, EdgeCases and the other five modules are added only after the slice is green, because Load
and Capacity cannot be cut and must work first. Parallel agents run in separate worktrees, two at most.
Every diff is reviewed against AGENTS.md before it is merged.

| # | Task | Who |
|---|---|---|
| T0 | NBomber API notes | controller |
| T1 | k3s infrastructure | controller, with the owner's root steps |
| T2 | `Basil.Tests.Shared.Environment` (Seed) | Sonnet |
| T3 | `Basil.Tests.Shared.Clients` | Sonnet |
| T4 | `Basil.Tests.Shared.Workload` | Sonnet |
| T5 | `Basil.Tests.Shared.Environment` (Monitor) and `Basil.Tests.LoadTests` (six modules) | Sonnet |
| T6 | k3s Jobs and run script | Haiku or OpenCode `deepseek-v4.1-flash` |
| T7 | PERF.md, architecture docs, solution, remove LoadHarness | Haiku |
| T8 | Smoke run | controller |

### T0: NBomber API notes (about 20 min)

* From Context7 and the NBomber 6.7 docs:
  * scenarios, steps, load simulations (KeepConstant, RampingConstant, Inject);
  * thresholds;
  * custom metrics (gauge, counter);
  * per-copy state;
  * HTML/CSV/MD reports;
  * the personal licence (whether a key or an acceptance is needed at run time), and whether thresholds and custom
    metrics are in the free tier; if not, server metrics go into files beside the NBomber report.
* Output: short API notes with a code sample per item, for the agents.
* Done when: each item has a sample.

### T1: k3s infrastructure (about 1.5 h)

* Prepare the data folder, write the manifests and `nginx-load.conf`.
* The owner enables the static CPU manager, imports the Basil image and sets the governor.
* Apply the namespace; wait for Postgres, Basil and nginx, and for the import of the 15 sets.
* Done when:
  * `GET /v1/beatmapsets` lists 15 sets;
  * `/v1/health` is `healthy` and `GET /v1/settings/ruleset/version` reports the stack installed;
  * a throwaway `hostNetwork` pod on cappuccino reaches `c.`, `osu.`, `api.` (8443) and IRC (6697) through
    192.168.27.124;
  * `kubectl describe` shows Basil, Postgres and nginx as Guaranteed with exclusive CPUs;
  * one IRC registration passes the PROXY check and one forwarded address reaches Basil;
  * the reset (restore, clear, restart) works once.

### T2: seed accounts and world data

* Sign in as an Owner, raise REST mutations through `PATCH /v1/settings/rateLimits`.
* Create accounts with `POST /v1/users`:
  * players: Player and Supporter;
  * referees and tourney: also Tournament;
  * one Developer account for the monitor.
* Put the limit back to its default.
* Write `world-data.json` (id, md5, length and mode of each beatmap) and `accounts.csv`, both outside git.
* Done when:
  * the settings read back at their defaults;
  * there are enough accounts for the expected ceiling plus a reserve;
  * every map has its md5 and length.

### T3: wire layer

* Move from LoadHarness: packet framing, osu! strings, the login body, the IRC client.
* Add packets:
  * multiplayer: create, join, part, slot, ready, start, load, score, complete, skip, fail, team, mods, settings, host,
    lock, invite;
  * spectating: 16, 17, 18;
  * tourney: 93, 108, 109.
* Read the answers: UserId, MatchJoinSuccess/Fail, UpdateMatch, MatchStart, AllPlayersLoaded, MatchComplete.
* OsuWeb:
  * getscores;
  * submission, with Rijndael-256, the checksum of `ScoreRecorder.cs` and the fingerprint of the login;
  * getreplay, search, `/d/`, screenshot, getfriends, seasonal, difficulty-rating.
* A REST client with an SSE reader.
* Done when:
  * packets built by the tool are read back correctly by the server's parser;
  * an encrypted score is decrypted correctly by Protocol's decryption.

### T4: world and personas

* A shared World: rooms, players in game, online users, beatmaps.
* State machines:
  * MatchRoom: players, IRC/REST referee, tourney, spectators;
  * FreePlayer, CasualRoom, Spectator, TourneyCluster, OverlayViewer, IrcChatter, Background, Churn, EdgeCases.
* Outcome classification.
* Done when:
  * each persona completes one cycle with one virtual user and no illegal transition;
  * each edge case gets the expected answer.

### T5: monitor and the six tests

* Scenarios per persona, steps per channel.
* Six test modules (Load, Stress, Spike, Soak, Capacity with the pilot, Scalability), each with its own load shape
  and SLO thresholds, behind one shared contract.
* The monitor feeds custom metrics from the diagnostics SSE.
* A fixed seed, written into the report.
* Done when:
  * `Basil.Tests.LoadTests <test>` runs that test alone;
  * the report has every step, threshold and server metric.

### T6: Jobs and run script

* Job templates for the generator (cappuccino) and the control load (espresso); the iperf3 server.
* `run.sh <test>`: publish, `scp` the binary to the node's hostPath, start the Job, wait, copy the report back, add the
  infrastructure record.
* Done when: one command runs a whole test through k3s and gathers every file into `reports/<test>-<timestamp>/`.

### T7: cleanup and docs

* PERF.md describes the new harness and how to run it.
* Remove `tools/LoadHarness` and its `/Tools/` entry in `Basil.slnx`; add the four `Basil.Tests.*` projects under
  `/Tests/`.
* AGENTS.md, `docs/for-developers/architecture.md` and `docs/for-developers/testing.md` name the four projects, their place (an
  outside client that references only Protocol) and why they are not under `tests/`.
* Done when: nothing refers to LoadHarness.

### T8: smoke run

* Two 2v2 rooms and 20 free players for two minutes.
* Server-side checks:
  * the matches have completed rounds;
  * each score is stored exactly once;
  * no session or room is left over.
* Done when: there is no `error`, every check holds, and the pilot can start on 2026-11-26.

## Verification

* `dotnet build Basil.slnx -c Release`; the project graph shows the `Basil.Tests.*` projects reaching Basil only
  through the two Protocol projects, and `Basil.Tests.Shared.Clients` free of NBomber.
* The smoke run (T8).
* Edge cases answer as expected: MatchJoinFail, Duplicate, a second login within 30 s.
* The generator is not the bottleneck:
  * cappuccino CPU stays below 70 % at N;
  * espresso's control latency moves with the main latency.

## Risks

* **No root on the workers:** the Jobs use a public image and a hostPath binary, so the workers need no change.
* **Static CPU manager restart:** restarting k3s on mocha restarts its pods; it is done once, before anything runs.
* **Traefik holds 80 and 443:** nginx serves on 8443 and 6697.
* **Wi‑Fi varies:** both workers are on Wi‑Fi at about 8 ms mean and 20–23 ms peak (an earlier 0.17 ms figure was
  measured to 192.168.110.68, which is not a worker). Packet loss shows as client timeouts, so the SLO and the error
  rate are judged from nginx on mocha; ping and iperf3 are recorded through every run.
* **Login lockout cascade:** all sessions share one address, and 50 failed logins lock it for 15 minutes. A run aborts
  on the first burst of refused logins.
* **Ramp length:** a login costs about 0.145 CPU-seconds (bcrypt), and every tourney child session logs in. The ramp is
  derived from N × 0.145 ÷ (Basil's cores × 0.5), so several thousand sessions need several minutes.
* **Git hygiene:** `accounts.csv`, `world-data.json`, the prepared data, the dump and `reports/` stay out of git.
* **mocha is the owner's workstation and the k3s control plane:** cores are pinned, heavy apps are closed, and the
  temperature is recorded.
* **cappuccino has 4 cores and may not carry a large N:** the pilot measures its CPU, and the owner is told before N is
  set.
* **Time:** T3–T5 are the largest block; the smoke run has to pass tonight.
