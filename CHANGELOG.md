# Changelog

All notable changes to Basil are listed here, newest first. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). A release's section is also its release notes: the release
workflow refuses a tag that has no section here.

## [Unreleased]

### Added

- Basil runs as a container behind nginx. nginx ends TLS on 443 and 6697, redirects port 80, and has a variant for
  servers behind Cloudflare. Docker Compose publishes only nginx. The image is built for amd64 and arm64.
- One `Data/` volume holds everything that must persist: settings, localizations, and stored files. `Cache/`, `Logs/` and
  `Temp/` sit beside it. `Data/metadata.json` versions the layout, and an older layout is upgraded on start.
- Each connection session (osu!, IRC, API) writes its own log file, kept for 7 days.
- The osu! calculation stack is loaded at run time from `Data/Rulesets/`, checked against a manifest on every start.
  Without it the server still runs; star ratings and pp stay empty and beatmapset imports are refused.
- Uploads are judged before their bytes are read: each upload route has its own size limit and minimum rate, and the
  caller is authorized first.
- The API names its errors: a missing or mistyped field answers 422 with the field, a refusal names the role that can
  act, and a 404 says what is missing. Request bodies and results are described in the OpenAPI document.
- Privileges and mods are lists of names in the API. The spectate stream sends replay and activity events.
- BasilBot says what happened after every command, and an unknown command points to `!help`. `!mp settings` shows size,
  host, referees and access.
- The osu! presence filter (packet 79) is honored for presence and stats broadcasts.
- A room waits for slow loaders at most 60 seconds after the first player has loaded, then starts without them.
- The console dashboard: dialogs take focus and close with Esc, flags are shown by name, sizes and durations carry units,
  the configuration shows where each value comes from, and `--ascii` draws plain ASCII.
- Sign-in failures are limited per address and account, with an overall cap per address.
- A load harness (`tools/LoadHarness`) and `PERF.md` for measuring the server under osu!, IRC and API load.

### Changed

- Search syntax names its values: `mode=taiko` (beatmaps and rooms) and `privilege=player,supporter` (users), the
  same names the API uses in JSON. Numbers are no longer accepted there.
- Secrets and deployment values (connection strings, domain, trusted proxies) come from the environment (`.env`), not
  from `appsettings.json`. The server stops at start with one line when the write connection string is missing.
- Client addresses come from nginx (`X-Real-IP`), and only a trusted proxy may supply them. Per-address rate limits live
  in nginx.
- IRC accepts a connection only when a trusted proxy opens it with a PROXY protocol header.
- Stored files are served by nginx when Basil names them.
- Slot numbers on the osu! wire start at 0 as in bancho.py. Every spectating bundle is forwarded, and the new host is told
  after a hand-off on leave. A player without the beatmap is not sent the match start.
- A room view rule decides who sees a room, the same for BasilBot, IRC, the API and the match list. A player seated in a
  room without a host becomes its host.
- A room-rule refusal answers 409 instead of 403. Idle limits depend on the session kind: osu! 60 seconds, IRC 300
  seconds, API 8 hours.
- IRC answers a failed command with 400, names the reason in 404 and 473, and no longer sends 422 at sign-in.
- The default texts of BasilBot and IRC are reworded in plain language. The version shown to clients has three parts.
- PostgreSQL data lives in a named volume (`postgres`); back it up with `pg_dump`.
- The server refuses to start when the database holds a migration it does not know.
- An unreachable server is named in one sentence by the console, `--help` exits 0, and an unknown argument is refused
  before anything starts.

### Removed

- Native self-update, the `restart` command and mDNS advertising. The update check only reports a newer release.
- Kestrel's own HTTPS, HSTS and the shipped certificate in the server; TLS is nginx's.
- The `Configurations/` folder and the `Templates/replies.json` setting; BasilBot replies are in
  `Data/Localizations/bot.json`.
- The osu-web style `chartUrl` in score submission answers.

### Security

- Failed sign-ins are counted per address and account, so one address cannot lock the owner out, and an IPv6 /64 counts as
  one source.
- An upload without a declared length is refused before its body is read.
- Screenshots and score uploads are accepted only from an address with an open osu! session, and the osu! error report
  is logged only for such an address.
- The default certificate for `basil.local` ships with a public private key: replace it before exposing a server.
