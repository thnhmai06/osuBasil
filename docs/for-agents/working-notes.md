# Working notes

Lessons that came up while restructuring Application and the hosts. They are not specific to osuBasil: the owner merges them into the code-principles skill later, and nobody edits the skill silently. Rules that bind this repository are in [`AGENTS.md`](../../AGENTS.md).

* **Read the IDE's suggestions** (see Code style): async overloads with the token in scope, LINQ where a loop reads as
  a sentence, return-switch over switch over if, ignore allocation hints and serializer-filled DTO reports.
* **Internal application logic is object-free; system Services are explicit contracts.** A helper that only implements a
  capability takes and returns records and plain results, never receives or creates a runtime object and never
  publishes an event. A service in a module's Services folder is different: it is a public system operation with a
  contract and implementation. `IAnticheatReporter` is the explicit event-source exception: its purpose is to report
  a live game's flags, and it is the single producer of anticheat events. Do not infer from this exception that internal
  helpers belong in Services. Coordination between objects otherwise belongs to the object acted on or its registry
  (`registry.LoginAsync`, `lobby.ReleaseAsync`, `scoreSubmitter.ScopeTo(connection).SubmitAsync`); a file full of
  former helpers ending up as `Server.X` is the God Actor in disguise. This supersedes the old rule "one child service per part".
* **Plain data that two layers both name in public signatures lives in the lower layer** (inputs, failure enums, import
  results), so the upper layer can expose it while the lower one stays internal.
* **Flip a reference direction in stages with a temporary internal bridge interface** declared in the upper project:
  move the logic, keep the call working through the interface, flip the reference, delete the interface.
* **Break a construction cycle with a deferred port** (`Func<T>`/`Lazy<T>` resolved at first use) rather than by
  letting one object build another inside its constructor; check the container with a harness that resolves every
  registration under a timeout, because a cycle shows up as a hang.
* **Detect the kind of an uploaded file from its content when the extension is unknown or when its size is needed**
  (ImageSharp header for images, ffprobe for audio); trust a known extension otherwise; no part of an uploaded name
  becomes a temporary file name. Cap what the input can cost (bytes while reading, pixels per side) before using it.
* **Give an architecture test a positive control**: after writing a no-dependency rule, assert once that the opposite
  claim (the project does depend on X) is detected, so a rule that always passes is caught.
* **Review reads the code, not only the green tests.** A passing suite proves the cases it covers; it does not prove
  that the code implements the contract, that every branch is reachable, or that a test was not weakened to pass. Read
  the diff line by line against the contract, the failure modes and the untested paths.
* **Delegated work is fast and time-boxed.** Give a subagent explicit files, a target state, what it must not change,
  the command that verifies it and a deadline (about 10 minutes for mechanical work, 20 for design). When the deadline
  passes, stop and report what was done instead of exploring outside the task.
* **Working with delegated agents:** stage explicit paths, never the whole tree; a worktree agent cannot use the IDE
  tools of the main checkout, so the orchestrator runs the container check after merging; two agents at a time at most,
  and only on disjoint files; review for the faults listed in "Final verification" item 8 and for a changed meaning
  hidden in a "style" rewrite (a `switch` turned into a nullable result plus an `if`).
* **Capabilities and outcomes** (revised; see `docs/for-agents/application-design.md`): a `Scoped*`
  capability is a `readonly struct` valid for one request that snapshots the effective privileges and states each
  requirement through `ScopedPrivileges.Lacks`; behaviours return the object's own outcome enum, whose `Denied = 0` is
  the refusal for lack of privileges (it does not name them) and whose `Ok = 1` is success. There is no shared
  `Result<T>`.
* **No `ConfigureAwait(false)`** in a server application (owner decision, 2026-10-26: the project rule stands and the
  skill's "every library await uses it" does not apply here).
* **Tests for computed values** pin properties (valid, finite, monotone under a modifier), not the exact number, unless
  the number is a protocol or ruleset contract; expected values come from the specification, never from running the code.
* **Test projects mirror source projects** one to one (`tests/` tree = `src/` tree, folders inside mirror feature
  folders); shared fixtures live in one support project.
* **A persistence layer has no SQL text** (owner decision, 2026-10-22): Entity Framework Core with LINQ and row objects,
  never Dapper or raw SQL (`FromSql`, `ExecuteSql`, `SqlQuery`, `NpgsqlCommand`); the few schema details EF cannot
  express (a filtered unique index, check constraints) are written in the model configuration and listed in the
  review. Rows are separate entity types, never the Domain types; migrations are EF migrations.
* **A fixed-id system account** (the bot, id 1) is taken over if it exists and created if it does not, by startup code,
  documented as the one exception to "the server creates no account".

* **Run the real binary before calling a host done** (2026-10-30). Tests that start the server inside the test process
  never execute `Program`, the exit path or the signal handling: the first run of the real executable found a crash
  at every stop, a fatal log line from a listener and an image route answering 500. Start it against a real database,
  create an account with the offline command, and exercise one request per protocol, one refusal, the stop signal and
  the exit code.
* **Resolve what the exit path needs before running the host.** The service provider is disposed when the host has
  stopped; reading a service from it afterwards throws and loses the exit code. Take the object first, run, then use it.
* **Read the memory-first store in the order that cannot lose a record.** (Being removed with the write-behind model:
  a repository that awaits its writes has no queue to snapshot. It stays only until the last module is migrated.)
* **Validate at the boundary what the consumer will need, and let the consumer survive what slipped through.** A
  header-only inspection accepts a picture that cannot be decoded; the serving side answers 404, not 500. Probe only the
  formats the server accepts, so the parsers of the others never see uploaded bytes.
* **Authorize before you look up.** A command or route that answers "no such X" before it checks the privilege to manage X
  tells an unauthorized caller which names exist. Check the privilege first; the refusal must be identical whether or not
  the target exists (same status, same missing-privilege mask).
* **Tests that share one running host own their identities.** A limit counted per address or per account is shared
  with every earlier test that is still closing sockets; give each limit test addresses or accounts nobody else uses.
* **Read a reply to its last line, and pace every poll.** A long list is split over several lines, so look at all of
  them. A loop that polls a fixed number of times without a pause ends before an asynchronous queue delivers; use the
  helper that pauses between polls.
* **A flake gets its failure message and a root cause, never a longer timeout or a retry.** Quiet test output hides the
  message: log to a result file (trx) and loop the test until it fails. Most of the flakes of the Host phase were the
  test reading too early or too little; one was a real race in production code.
* **Adding a member to an enum means running the exhaustive tables.** Run the whole test project of every area the
  change touches, not the filtered tests of the feature: a table that lists every outcome only goes red there.
* **Delegated agents are hands; the orchestrator decides how.** Write the files, the exact change, the cases and the
  verification into the prompt, pass it as a file, bound it with a timeout and have it write an exit marker (a long
  prompt passed on the command line hung a run for seven hours). Read the agent's diff, not its summary.
* **Re-verify a suspected defect in the code before dispatching it.** A handover list is a hypothesis: three of its
  fifteen items were already handled (the code closed the connection, or the lock had nothing left to block).
* **Check the license before bumping a dependency to a fixed version.** A security release can change its terms; the
  version that fixes the advisories may not build without a key, which is the owner's decision, not a build detail.

Proposals from the modular-monolith redesign, for the owner to merge into the skill (nobody edits the skill silently):

* **Define the layers by lifecycle, not by technology.** Domain is the system at rest (what is stored and the rules true
  offline); Application is the contracts plus the running system; Infrastructure implements the contracts. A type that
  only means something while running is Application, however plain it looks.
* **Separate "who" from "what".** Services are the doers (verbs with a contract); stores and objects are the things.
  Order them Service > Ports > Registry | Repository | Storage > Objects, use direct injection across levels and a
  mediator between peers.
* **Never await a notification under a lock.** Objects enqueue events while they hold their lock; a dispatcher publishes
  outside it.
* **A translation layer is a language, not a service.** Protocol is the vocabulary of a wire; the host translates and
  dispatches, and the message type (not the host) chooses the handler. The actor travels in the message as an
  unforgeable object created by one module only.
* **Admission control is a leaf and knows no business.** Allow/Deny from size, rate, concurrency and load, in two tiers,
  with an opaque subject for the second; every entry point must declare a policy and a test fails when one does not.
* **Persistence is awaited.** Prefer cache-aside over write-behind; reads are bounded, a listing never instantiates live
  objects, and a large payload never passes through a cache.
* **Streaming is a rule, not an optimization.** Spool uploads to a temporary file, validate, move atomically; no
  `MemoryStream` in the application layer.
* **An architecture test over source text needs a positive control.** The ban on `MemoryStream` in Application scans
  `src/Application/**/*.cs` (reflection cannot see a local variable) and a temporary file that contains it proves the
  scan can fail.
* **Keep what validation refused, briefly.** A refused upload moves to a quarantine under a name of the server's own
  and is swept after a week, so an operator can look at it and the disk cannot fill with it.
* **A listing that only decides "seen or not" asks for ids.** The scanner lists ids bounded by the list ceiling and
  loads only the few rows it forgets.

