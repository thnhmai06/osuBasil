# W7: the osu! (stable) wire (roadmap E3)

Source of truth: `plans/review/release-1.0.md`, roadmap group **E3** and its rows: F-044, F-033, F-058, F-054, F-040
(D19), F-059, F-109, F-108 (edge half), F-110, F-095 (D35), F-094 (both halves). Read each row and decision first, and
`AGENTS.md` ("Stable protocol", the Protocol paragraph of "Architecture", "Hosts, Gateway and Server"). bancho.py
(osuAkatsuki/bancho.py: `app/packets.py`, `app/api/domains/cho.py`, `app/api/domains/osu.py`, `app/objects/match.py`)
is the reference for every byte and every behaviour; when Basil and bancho.py differ on the wire, bancho.py wins unless
a review decision says otherwise. W6 is committed (`6cdff7d8a`): the room's number is the match id, `Room.WireId` is the
runtime id used only on the wire.

## Items, in order

1. **F-044, F-033:** slot numbers converted at the Stable host boundary in both directions, matching bancho.py's 0-based
   ids; every packet that carries a slot (change slot, lock, kick, team, transfer host, the room packet arrays) uses
   one conversion.
2. **F-058, F-054:** the start answer (`MatchStart`, then the per-slot status) decoded and sent as bancho.py does, after
   the slot fix.
3. **F-040 (D19):** every spectating action forwarded; one frames-and-score pair per bundle, as D19 says.
4. **F-059:** `osu-error.php` accepted (200, empty) and logged at Information with the useful fields.
5. **F-109:** the `ErrorReport` client packet read and logged.
6. **F-108 (edge half):** presence and stats deliveries to an osu! session follow its stored `PresenceFilter` (none,
   all, friends) as bancho.py does.
7. **W6 leftover:** when the host passes to another player because the host left (W6's handoff), the new host's
   client gets `MatchTransferHost` as on any other host change.
8. **F-110:** the submission chart answer carries no `chartUrl`; `/u`, `/b`, `/beatmapsets` stay 404.
9. **F-095 (D35):** the osu-web answers (the pipe/newline formats of `osu-osz2-getscores.php`, `osu-search.php`,
   `osu-search-set.php`, the submission charts, `osu-getbeatmapinfo.php`, …) are written by writers in
   `Basil.Protocol.Game.Stable` from plain DTOs; `Basil.Host.Stable` maps its views to those DTOs and only calls them.
10. **F-094:** the `osump://` room link and its embed leave `Basil.Application.Multiplayer` (`Room.Url`/`UrlEmbed`,
    `Room.cs:59-61` before W6); Protocol writes the link from the wire id; Domain gets an "action" flag on a chat message
    instead of letting `\u0001` into text (`Message.cs:48`), and the Stable host writes the `ACTION` framing from that
    flag. Find every producer and consumer of the old link and the raw `\u0001`.

## Binding rules

- Protocol translates and validates nothing; the host checks numbers with `WireMap` before Application sees them.
- No Domain enum is renumbered for the client; the mapping lives in the host or Protocol.
- A packet layout change is a protocol change: write in the report every packet whose bytes changed and why, with the
  bancho.py line that justifies it.
- Text the osu! client shows lives in the Stable reply class (`OsuWebReplies` or the like), short and plain.

## Rules for you

Rider MCP (`search_symbol`, `find_usages`, `rename_refactoring`, `move_type_to_namespace`, `safe_delete`,
`get_file_problems`, `build_solution_*`), OpenCode Go at `http://localhost:4096` with cheap flash models for mechanical
edits only. Build only (`dotnet build Basil.slnx -c Release --nologo -v q`), no tests (deferred; ignore `tests/`), no
commit, no `git add`, do not touch `Basil/Data/appsettings.json`. An item needing an unwritten decision: skip it,
finish the rest, report.

## Report

Per item: what changed, files, and for wire changes the packets whose bytes changed with the bancho.py reference;
skipped items and why; decisions you took; build result.
