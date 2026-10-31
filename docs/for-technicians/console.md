# Console

The console is the operator's entry point to a Basil server: a terminal dashboard with a command box, and the same
commands from a shell. This page is the authoritative specification of the console commands, their output and their exit
codes. The HTTP routes they mirror are described by the generated API reference, not here.

## Why

Whoever sits at the console owns the machine, so the console is the one entry point that skips the permission checks.
It is how the first administrator is created (the server never creates an account by itself), how an operator repairs a
server whose accounts cannot sign in, and how a deployment script prepares the server before it starts. It is not a chat
surface: BasilBot's chat commands are separate and keep checking the typist's rights.

## Modes

| Mode             | Argument      | Shows                                      | Command box |
|------------------|---------------|--------------------------------------------|-------------|
| Full (default)   | none          | dashboard and command box                  | yes         |
| No console       | `--noconsole` | dashboard only                             | no          |
| Log only         | `--nogui`     | linear log lines on stdout, no colour      | no          |

* `--nogui` wins when it is given together with `--noconsole`.
* The server switches to `--nogui` by itself when stdin or stdout is redirected (Docker, systemd) or the terminal has no
  colour support.
* Ctrl+C: the first press clears the line being typed; with nothing typed (always, in `--noconsole`) it warns and opens a
  3 second window in which a second press starts the graceful shutdown. In `--nogui` the first press starts the graceful
  shutdown. While shutting down nothing ends the process for 30 seconds; after that a warning appears and one more press
  forces the exit (data loss may occur). `SIGTERM` starts the graceful shutdown at once, in every mode.

## Running a command

A command can be typed in the command box or given on the command line of the executable:

```text
Basil.Host users create --username admin --password secret --permissions 1048575
```

Both use the same parser, print the same output and return the same exit code.

| The server is...  | What happens                                                                                           |
|-------------------|--------------------------------------------------------------------------------------------------------|
| running           | the command is forwarded over the local socket `Data/basil.sock` and the output streams back           |
| not running       | only commands with **P = yes** run, directly against the storage; any other command fails with code 3  |

### The local socket

* A Unix domain socket at `Data/basil.sock`, readable and writable by the account that runs the server only (mode `0600`).
  It also works on Windows 10 and later.
* It exists only while the server runs. On startup the server first tries to connect to an existing file: if something
  answers, another server is running and this one refuses to start with a clear error; only if the connection is
  refused is the leftover file of a crashed server deleted and replaced. A client treats a refused connection as "the
  server is not running".
* The path of a Unix socket is limited to about 108 bytes. If `Data/` is too deep the server logs a clear error and runs
  without the socket (shell commands then fall back to offline mode, which is unsafe while the server runs, so keep the
  data directory path short).
* The protocol is one request and one streamed reply: the client sends the argument list as one UTF-8 line (a JSON array
  of strings); the server runs it as the operator and streams the plain text output framed per line: every output line
  is sent as `o <text>` and the reply ends with `x <exit code>`, so output can never be mistaken for the exit code.
  The socket has no interactive questions: the confirmation of `shutdown` and `restart` is asked in the calling terminal
  before the request is sent.
* The command runs on the machine of the server only. Calling from another machine or container is not supported; in
  Docker use `docker exec <container> Basil.Host <command>`.

### Running offline

With the server stopped, a command with P = yes starts only the storage: the directories are created, the database is
migrated (an empty database too), the stored data is loaded. The running state is not recovered and no listener is
opened. After the command the write queue is drained and flushed before the process exits, so a command that printed its
confirmation has stored its change.

> **Never run an offline command while another server uses the same database.** The offline process writes straight to
> the database, and the memory-first data of the running server overwrites it.

### Exit codes of a command

| Code | Meaning                                                                                  |
|------|------------------------------------------------------------------------------------------|
| 0    | success                                                                                  |
| 1    | the operation was refused: not found, conflict, gone, locked, system account, forbidden  |
| 2    | the input is not valid, or the command line cannot be understood                         |
| 3    | the server is not running and the command needs it                                       |
| 4    | the operator declined the confirmation                                                   |

The refusal is printed as `error: <code>: <message>`, with the same camelCase code the HTTP API uses (`notFound`,
`conflict`, `systemAccount`, `invalid`, ...). `permissionDenied` never happens, because the console skips the checks, and
`locked` never happens either, because the console passes a lock as a manager does.

### Exit codes of the process

| Code | Meaning                                                              |
|------|----------------------------------------------------------------------|
| 0    | the server shut down normally                                        |
| 1    | the server stopped because of an error                               |
| 75   | the server asks its manager to start it again (`restart`)            |

The server is under a manager when the environment variable `INVOCATION_ID` (systemd) is set or
`DOTNET_RUNNING_IN_CONTAINER` is `true`. Under a manager `restart` exits with 75 and does not start a second instance
itself. Docker's restart policy
`always` and `unless-stopped` start the container again after any exit code; `on-failure` only after a non-zero one, so
75 restarts it and a normal shutdown (0) does not. Outside a manager, `restart` starts the same executable with the same
arguments after the shutdown finishes; the new process retries to bind its ports for a few seconds, because the old one
may still hold them.

## The mapping rule

Console commands mirror the HTTP API one to one. There is a single rule, not a syntax per command:

| HTTP route                                       | Console command                                                 |
|--------------------------------------------------|-----------------------------------------------------------------|
| `GET /v1/x?a=1&b=2`                              | `x list --a 1 --b 2`                                            |
| `GET /v1/x/{id}`                                 | `x <id>`                                                        |
| `POST /v1/x` with body `{a, b}`                  | `x create --a 1 --b 2`                                          |
| `PATCH /v1/x/{id}`                               | `x <id> set --a 1 ...` (only the given fields change)           |
| `PUT /v1/x/{id}/y` with body `{value}`           | `x <id> y --value 1` (one option per body field)                |
| `DELETE /v1/x/{id}`                              | `x <id> delete`                                                 |
| `POST /v1/x/{id}/cmd`                            | `x <id> cmd --field 1`                                          |
| `GET /v1/x/{id}/y`, `.../{yid}`, `DELETE`        | `x <id> y list`, `x <id> y <yid>`, `x <id> y <yid> delete`      |

* Option names are the query parameter or body field names of the API, in camelCase (`--pmPrivate`, `--readPrivileges`).
* Values have the API's types. **Flags enums are numeric bitmasks**, never names (`--permissions 1048575`, `--types 12`);
  an ordinary enum is its camelCase name; a boolean is `true` or `false`; a duration is `30m`, `12h` or `7d`.
* Every list takes `--offset` and `--limit` as the API does (default 0 and 25, at most 100).
* A route that returns a file or a stream (replays, beatmap files, avatars, the live `/live` and match streams) and the
  routes of one's own session (`/v1/auth/*`, `/v1/me`, `/v1/sessions`, the IRC key of the calling account) have no
  command: the console is not signed in.

## Output

The output is for people and never JSON; there is no `--json` option.

* One object is a list of aligned `label: value` lines.
* A list is a table with a header row.
* A bitmask is followed by its flag names: `permissions: 12 (ChatPost, ChatPostPrivate)`.
* Times are shown in the operator's local time zone (injected, so the output of a test does not depend on the machine).
* The result of an action is one confirmation line.
* In the dashboard the output is coloured with the theme; from a shell, over the socket and in `--nogui` it is plain
  text without colour.

## The dashboard

| Key                    | Does                                                                                   |
|------------------------|----------------------------------------------------------------------------------------|
| `F1` to `F6`           | Overview, Users, Rooms, Metrics, Logs, Chat                                            |
| `Alt+Up`, `Alt+Down`   | Select a row of the Users or Rooms table                                               |
| `Alt+Enter`            | Put the id of the selected row in the command box                                      |
| `PageUp`, `PageDown`   | Freeze the log, follow the log                                                         |
| `Tab`                  | Complete the command; the ids that exist are offered where a command takes one         |
| `Ctrl+C`               | See [Modes](#modes)                                                                    |

Graphs are drawn with braille characters (2 by 4 dots per cell) and coloured by value; a terminal narrower than 100
columns hides the big graphs. The history is 15 minutes, one sample per second, and comes from the same sample as the
HTTP diagnostics. Per-core CPU is shown on Linux only.

## Commands

**P** says whether the command runs with the server stopped (**yes**) or needs it running (**no**).

### Console only

| Command                      | Description                                                                                            | P   |
|------------------------------|--------------------------------------------------------------------------------------------------------|-----|
| `help [<command>]`           | Lists the commands; with a command name shows its syntax, options and the API route it mirrors.        | yes |
| `status`                     | Version, uptime, online connections by kind, rooms and rounds in progress.                             | no  |
| `shutdown [--now]`           | Graceful shutdown. Without `--now` it asks for confirmation (in the calling terminal from a shell).    | no  |
| `restart [--now]`            | Graceful shutdown that ends with the restart exit code. Asks for confirmation like `shutdown`.         | no  |
| `theme ["<scheme>"]`         | Lists the colour schemes or changes the dashboard theme and saves it. Dashboard only. A name with spaces is written in quotes. | no  |

### users

| Command                                                                                             | API route                                                     | P   |
|-----------------------------------------------------------------------------------------------------|---------------------------------------------------------------|-----|
| `users list [--query <q>] [--offset] [--limit]`                                                     | `GET /v1/users`                                               | yes |
| `users create --username <name> --password <pw> [--country <cc>] [--permissions <mask>] [--admin]`  | `POST /v1/users`                                              | yes |
| `users <id>`                                                                                        | `GET /v1/users/{id}`                                          | yes |
| `users <id> set [--username] [--country] [--away] [--pmPrivate] [--permissions <mask>] [--password <pw>]` | `PATCH /v1/users/{id}`; `--password` is `PUT .../password` | yes |
| `users <id> avatar --file <path>`, `users <id> avatar delete`                                       | `PUT`, `DELETE /v1/users/{id}/avatar`                         | yes |
| `users <id> friends list`, `friends <fid> put`, `friends <fid> delete`                              | `/v1/users/{id}/friends[/{fid}]`                              | yes |
| `users <id> blocks list`, `blocks <bid> put`, `blocks <bid> delete`                                 | `/v1/users/{id}/blocks[/{bid}]`                               | yes |
| `users <id> restrictions list`                                                                      | `GET /v1/users/{id}/restrictions`                             | yes |
| `users <id> restrictions create --permissions <mask> [--duration <d>] [--reason <text>]`            | `POST /v1/users/{id}/restrictions`                            | yes |
| `users <id> restrictions <rid> lift`                                                                | `POST .../restrictions/{rid}/lift`                            | yes |
| `users <id> restrictions <rid> delete`                                                              | `DELETE .../restrictions/{rid}`                               | yes |
| `users <id> sessions list [--state all\|online\|offline] [--types <mask>]`                          | `GET /v1/users/{id}/sessions`                                 | yes |
| `users <id> sessions <sid>`                                                                         | `GET /v1/users/{id}/sessions/{sid}`                           | yes |
| `users <id> sessions <sid> delete`                                                                  | `DELETE /v1/users/{id}/sessions/{sid}`                        | yes |
| `users <id> sessions delete`                                                                        | `DELETE /v1/users/{id}/sessions` (none is kept)               | yes |
| `users <id> delete`                                                                                 | `DELETE /v1/users/{id}`                                       | yes |

`--password` is the plain password; the server hashes it as it does for the API. `--admin` is a convenience that gives the account every permission (deployment scripts use it to create the first administrator); `-u` and `-p` are short names of `--username` and `--password`. `--duration` replaces the API's
`endsAt`: the restriction ends that long from now, and no duration means permanent. `--password` and `--new` are masked
in the log, in the history and in suggestions.

### channels

| Command                                                                                                           | API route                     | P   |
|-------------------------------------------------------------------------------------------------------------------|-------------------------------|-----|
| `channels list`                                                                                                   | `GET /v1/channels`            | yes |
| `channels create --name <#name> [--topic] [--readPrivileges <mask>] [--writePrivileges <mask>] [--autoJoin] [--visible]` | `POST /v1/channels`     | yes |
| `channels <name> set [--topic] [--readPrivileges <mask>] [--writePrivileges <mask>] [--autoJoin] [--visible]`      | `PATCH /v1/channels/{name}`   | yes |
| `channels <name> delete`                                                                                          | `DELETE /v1/channels/{name}`  | yes |

### rooms

Rooms exist only while the server runs.

| Command                                                          | API route                          | P  |
|------------------------------------------------------------------|------------------------------------|----|
| `rooms list [--offset] [--limit]`                                | `GET /v1/rooms`                    | no |
| `rooms create --name <name> [--password] [--private]`            | `POST /v1/rooms`                   | no |
| `rooms <id>`                                                     | `GET /v1/rooms/{id}`               | no |
| `rooms <id> <command> [--field ...]`                             | `POST /v1/rooms/{id}/<command>`    | no |

`<command>` is one of `close`, `lock`, `unlock`, `size`, `move`, `host`, `clearhost`, `set`, `start`, `abort`,
`timer`, `aborttimer`, `team`, `map`, `mods`, `kick`, `ban`, `unban`, `invite`, `password`, `name`, `private`, `addref`,
`removeref`; its options are the body fields of that route (`rooms 5 start --seconds 10`, `rooms 5 move --user 3 --slot 2`,
`rooms 5 mods --mods HD,DT`). The console is the room's creator for the purposes of authority; it never becomes its host.

### matches

| Command                                    | API route                                                          | P   |
|--------------------------------------------|--------------------------------------------------------------------|-----|
| `matches list [--query] [--offset] [--limit]` | `GET /v1/matches`                                               | yes |
| `matches <id>`                             | the first snapshot of `GET /v1/matches/{id}`; the live stream is not followed | yes |

### beatmapsets and beatmaps

| Command                                                                          | API route                           | P   |
|----------------------------------------------------------------------------------|-------------------------------------|-----|
| `beatmapsets list [--query <q>] [--offset] [--limit]`                            | `GET /v1/beatmapsets`               | yes |
| `beatmapsets <id>`                                                               | `GET /v1/beatmapsets/{id}`          | yes |
| `beatmapsets create --file <path.osz>`                                           | `POST /v1/beatmapsets`              | yes |
| `beatmapsets <id> set [--status <status>] [--visible <bool>] [--locked <bool>]`  | `PATCH /v1/beatmapsets/{id}`        | yes |
| `beatmapsets <id> delete`                                                        | `DELETE /v1/beatmapsets/{id}`       | yes |
| `beatmapsets refresh`                                                            | console only                        | yes |
| `beatmaps <id>`                                                                  | `GET /v1/beatmaps/{id}`             | yes |

`beatmapsets refresh` scans `Data/Beatmapsets` as at startup: sets added by hand are picked up and sets removed from the
folder are dropped. Changing a set's status can change how clients submit scores and replays; prefer a status that has a
leaderboard (ranked, approved, qualified, loved).

### scores

| Command                                                          | API route               | P   |
|------------------------------------------------------------------|-------------------------|-----|
| `scores list [--beatmap <id>] [--user <id>] [--offset] [--limit]` | `GET /v1/scores`       | yes |
| `scores <id>`                                                    | `GET /v1/scores/{id}`   | yes |

### menu, faqs, settings, announce, screenshots

| Command                                                                                  | API route                           | P   |
|------------------------------------------------------------------------------------------|-------------------------------------|-----|
| `menu banners list`, `menu banners create --image <path> --url <url> [--begins] [--expires]`, `menu banners <image> delete` | `/v1/menu/banners` | yes |
| `menu seasonals list`, `menu seasonals create --image <path>`, `menu seasonals <image> delete` | `/v1/menu/seasonals`          | yes |
| `menu icon`, `menu icon set [--image <path>] [--url <url>]`, `menu icon delete`           | `/v1/menu/icon`                     | yes |
| `faqs list`, `faqs <entry>`, `faqs <entry> set --file <path>`, `faqs <entry> delete`     | `/v1/faqs[/{entry}]`                | yes |
| `settings`                                                                               | `GET /v1/settings`                  | yes |
| `settings <category>`                                                                    | `GET /v1/settings/{category}`       | yes |
| `settings <category> set --<field> <value> ...`                                          | `PATCH /v1/settings/{category}`     | yes |
| `announce create --text <text> [--to <id,id,...>]`                                       | `POST /v1/announce`                 | no  |
| `screenshots list [--user <id>] [--offset] [--limit]`                                    | `GET /v1/screenshots`               | yes |
| `screenshots <id> delete`                                                                | `DELETE /v1/screenshots/{id}`       | yes |

A nested settings field is written with dots: `settings rateLimits set --chat.count 5`. The locks category takes the
four locks as booleans (`settings locks set --accounts true`): where the API shows a flags enum as booleans, so does the
console; everywhere else flags are numeric bitmasks.

### diagnostics

There is no command that shows diagnostics; the dashboard shows them. There are the actions only.

| Command                      | API route                                    | P   |
|------------------------------|----------------------------------------------|-----|
| `diagnostics gc collect`     | `POST /v1/diagnostics/gc/collect`            | no  |
| `diagnostics caches clear`   | `POST /v1/diagnostics/caches/clear`          | yes |
| `diagnostics exceptions reset` | `POST /v1/diagnostics/exceptions/reset`    | no  |

## Shutdown and restart

`shutdown` and `restart` have no HTTP route and no chat command. Both run the graceful shutdown of the server. Typed in
the dashboard without `--now` they ask `y/N` in the command box. Given on the command line of a running server without
`--now` the question is asked in the calling terminal before anything is sent; declining ends with code 4.

## Design and invariants

* Commands run as BasilBot with the operator's connection, which skips the rights checks; this exists only on the
  console path. BasilBot's own connection and the chat commands keep their normal limits.
* Every command is built from the same Application behaviours as the HTTP route it mirrors, and prints the same record
  view. A test requires a command for every route, except the ones listed under the mapping rule.
* A password, a new password or a key never appears in the log, the history, the suggestions or the output.

## Related code

* `src/Hosts/Cli` - the dashboard, the command parser, the runner and the socket.
* `src/Hosts/Shared` - the record views and the outcome table both HTTP and console use.
* [`configuration.md`](configuration.md) - the data directory the socket lives in.
