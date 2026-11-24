# W8: architecture and IRC (roadmap C4 and E4)

Source of truth: `plans/review/release-1.0.md`, roadmap groups **C4** and **E4** and their rows: F-100, F-081, F-084
(core half), F-086 (D16), F-101, F-093 (D35), F-043, F-018, F-015 (D20), F-016 (IRC half), F-080, and D26/D32 for the
IRC reply file. Read each row first, and `AGENTS.md` in full (above all "Architecture", "Hosts, Gateway and Server",
"Domain and Application", "Final verification" 7 and 8) and `docs/for-agents/module-layout.md`. W1 to W7 are committed
(last `2baa5b389`); W7 already moved the `ACTION` framing into `Basil.Protocol.Irc/CtcpAction.cs`.

## C4, architecture first

1. **F-100:** every change of state a host asks for goes through a Mediator message handled in Infrastructure (login,
   close, mark active, logout, and any other the hosts do today by calling a registry or a live object directly). The
   hosts keep lookups only. Grep every host (`Basil.Host.*`) for calls that mutate registries or live objects and list
   them in the report with the message that replaces each.
2. **F-081:** one handler per event type, no state in handlers (`RoomAnnouncer`, `ConnectionChannelHandler` and any
   other that serves several event types or keeps fields).
3. **F-084 (core half):** split `SessionRegistry` by responsibility (as the row describes); remove the remaining
   `new Md5(` and the bare `catch` around `Parse`; make `docs/for-developers/migration-status.md` true; options bound by
   the project that defines them (`Add<Module>...(IConfiguration)`), the composition root only choosing what to run.
4. **F-086 (D16):** neutral names for `OsuClient`, `OsuFlags`, `OsuFingerprint` in `Basil.Domain/Client/` (Rider
   `rename_refactoring`; propose the names in the report if the row does not give them, choose short ones that say what,
   not who: e.g. `ClientBuild`, `ClientFlags`, `ClientFingerprint`). Session kind names stay.
5. **F-101:** Domain loses its unused package references (`Logging.Abstractions`, `Options`); `Basil.Shared` is added to
   the project graph in `AGENTS.md` with what it may reference.

## E4, IRC

6. **F-093 (D35):** the IRC wire moves from `Basil.Host.Irc` into `Basil.Protocol.Irc`: line framing and CRLF
   (`IrcLines`, `IrcSocket` framing), the 512-byte limits now in `IrcReplies`, `IrcText` splitting by bytes without
   cutting UTF-8, the raw verb and numeric strings (constants), ISUPPORT writing (`IrcCommands.cs:196`), SASL `PLAIN`
   decoding (`:415`), `CAP` encoding, the nick!user@host mask. `IrcReplies` keeps texts only. Protocol references nothing.
7. **F-043:** a failed command is answered (the numeric the RFC or the IRCv3 spec gives for that failure), never
   silence.
8. **F-018:** answer a client's housekeeping as clients expect (`221` for `MODE <own nick>`, `368` end of ban list for
   `MODE #chan +b`), and keep `482` only for a real change attempt.
9. **F-015 (D20):** `404` and `473` name the reason in their text.
10. **F-016 (IRC half):** `QUIT` without a reason is accepted; `002` names the release version; no `422` when there is no
    message of the day.
11. **F-080 and D26/D32:** every client-visible IRC and Stable literal lives in its surface's reply class. IRC replies
    are read from `Data/Localizations/irc.json` with named placeholders through the same catalog mechanism BasilBot
    uses since W3 (`Basil.Infrastructure.Bot/Services/ReplyCatalog.cs`); move what is shared to where both can use it
    without breaking the module graph, or give the IRC host its own small catalog of the same shape; the existing
    `Basil/Data/Localizations/irc.json` sample may be replaced by the catalog's default dump. Do not reword: keep every
    default text as it is today (the reviewer handles wording).

## Rules for you

This wave is broad: split it yourself into sub-steps in the order above, and hand clearly specified mechanical
sub-tasks (moves, renames, constant extraction) to OpenCode Go at `http://localhost:4096` with cheap flash models; you
own and review the result. Use Rider MCP (`search_symbol`, `find_usages`, `rename_refactoring`, `move_type_to_namespace`,
`change_api_signature`, `safe_delete`, `get_file_problems`, `build_solution_*`). Build only
(`dotnet build Basil.slnx -c Release --nologo -v q`) after each sub-step, no tests (deferred; ignore `tests/`), no
commit, no `git add`, do not touch `Basil/Data/appsettings.json`. An item needing an unwritten decision: skip it,
finish the rest, report.

## Report

Per item: what changed and the files; for F-100 the list of host mutations and their messages; names chosen for F-086;
skipped items and why; decisions you took; build result.
