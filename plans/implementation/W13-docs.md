# W13: documentation (roadmap E8)

Source of truth: `plans/review/release-1.0.md`, roadmap group **E8** and its rows F-097, F-104, F-064, F-066, F-065,
F-031, F-015 (doc half), F-005, F-061, F-001, F-004, F-003, F-062, and D24's documentation half; the Progress table at
the end lists what W1 to W12 changed (last commit `a673d5065`). Read `AGENTS.md` rule 7, `docs/index.md` and
`docs/for-developers/docs-guideline.md` first. The code is the truth: when a page and the code disagree, the page
changes; when you cannot tell what the code does, say so in the report instead of guessing.

## Items, in order

1. F-097: `docs/index.md` names one owning page for every edge decision D26 to D37 (layout, nginx, TLS, PROXY, uploads,
   configuration); create a page only when no existing page fits.
2. F-104: `AGENTS.md` matches what landed in W1 to W12 (Gateway enforcement status, IRC behind nginx, `Data/` layout,
   uploads, configuration split, reply catalogs, relationship registry). Remove "(target)" marks for what is done; keep
   them for what is not.
3. F-064: features out of scope (`working-scopes.md`) are not described as existing anywhere in `docs/`.
4. F-066: no documented behaviour that does not exist; every relative link in `docs/`, `README.md`, `AGENTS.md`,
   `PERF.md`, `CHANGELOG.md` resolves (write a small script in your scratch area to check links; do not commit it).
5. F-065: contradicting pages reconciled (the `irc.` DNS record is required, D34).
6. F-031: `docs/for-client/api/response-envelope.md` matches the runtime envelope (read the middleware and the schema
   transformer).
7. F-015 (doc half), F-005: the IRC guide is true and public (TLS on 6697 through nginx, SASL, what the server does not
   support).
8. F-061: contributor first-run commands that work: `dotnet run` for development and the compose stack with nginx;
   the commands in `development.md` and `README.md` are ones that run today.
9. F-001, F-004, F-003: `working-scopes.md` up to date with what exists now.
10. D24 docs half: `docs-guideline.md` follows rule 7; client starter material: a sample IRC client configuration
    (Irssi or WeeChat) and a short osu! stable client guide (how to point the client at a Basil server, `-devserver`).
11. `docs/for-developers/migration-status.md`: bring it up to date with W1 to W12.

## Rules for you

Documentation only: no change to `.cs`, `.csproj`, workflows, compose or nginx files; if docs reveal a code bug, list it
in the report. Plain prose, one idea per paragraph, Why → What → How, no pasted source, no second hand-written API, IRC
or BasilBot reference (they are generated). Do not touch `Basil/Data/appsettings.json`, do not commit or `git add`,
no tests. For mechanical sub-tasks (link fixing across many files) you may use OpenCode Go at `http://localhost:4096`
with cheap flash models; review what it writes. Rider MCP `search_symbol`/`find_usages` help confirm what the code does.

## Report

Per item: pages changed; code bugs found; claims you could not verify; decisions you took.
