# Console

The console is the operator's entry point to a Basil server: a terminal dashboard with a command box, and the same
commands from a shell. This page is the authoritative specification of the console commands, their output and their exit
codes. The HTTP routes they mirror are described by the generated API reference, not here.

## Why

Whoever sits at the console owns the machine, so the console is the one entry point that skips the privilege checks.
It is how the first administrator is created (the server never creates an account by itself), how an operator repairs a
server whose accounts cannot sign in, and how a deployment script prepares the server before it starts. It is not a chat
surface: BasilBot's chat commands are separate and keep checking the typist's privileges.

## Modes

| Level          | Argument      | Shows                                    | Commands                                                              |
|----------------|---------------|------------------------------------------|-----------------------------------------------------------------------|
| Full (default) | `--console 1` | dashboard and command box                | typed in the command box, or from a shell                             |
| Log only       | `--console 0` | linear log lines on stdout, no dashboard | from a shell only, through the local socket of the running server    |

Two more arguments change how the dashboard looks:

* `--nocolor` makes it monochrome with a fixed theme; the `theme` command is refused. The `NO_COLOR` environment variable
  turns it on too.
* `--ascii` draws with ASCII characters only: frames, dialogs, scroll bars (hidden), graphs and text.

* The server switches to `--console 0` by itself when stdin or stdout is redirected (Docker, systemd) or `TERM` is `dumb`.
* `--nogui` and `--noconsole` no longer exist; the server refuses them.
* Ctrl+C: the first press clears the line being typed; with nothing typed it warns and opens a
  3 second window in which a second press starts the graceful shutdown. With `--console 0` the first press starts the graceful
  shutdown. While shutting down nothing ends the process for 30 seconds; after that a warning appears and one more press
  forces the exit (data loss may occur). `SIGTERM` starts the graceful shutdown at once, in every mode. In an attached container the dashboard also shows
  `Ctrl+P Ctrl+Q detach` in its key hints: that leaves the server running, while two `Ctrl+C` stop it. However the dashboard
  ends, the terminal is given back (main screen, cursor, mouse and colours).

## The dashboard

The dashboard is a server-side admin view built so that an operator can look at the server and act on it without
leaving the terminal. It shows ten tabs on one line, the page of the tab in view, and a line of key hints. Keys and mouse
are listed under [Keys and mouse](#keys-and-mouse).

| Tab      | Shows                                                                                                     |
|----------|-----------------------------------------------------------------------------------------------------------|
| Console  | The log written straight on the screen and, at `--console 1`, the command box                             |
| Monitor  | The server's own CPU, memory and connection graphs, then one cell per diagnostics category                |
| Users    | Accounts online and offline                                                                               |
| Beatmaps | Beatmaps grouped by set; actions apply to the whole set                                                   |
| Lobby    | Open rooms and the ones that ended; creators and referees manage a room from here                         |
| Chat     | Channels, their messages since the server started, and their members; messages are sent as BasilBot       |
| Scores   | Scores, newest first, searched with `user:<id or name> beatmap:<id or query>`                             |
| Sessions | The open sessions of osu! stable, Tourney, IRC and the HTTP API, one group per kind                       |
| Settings | Server, Accounts and Gameplay forms saved field by field, and the read-only configuration                 |
| About    | Version, licence and the update status, with a button to check now                                        |

Sessions replaces the former Connections tab; the graphs of the connections moved to Monitor. Every action of every tab
is a console command.

### Tabs and layout

Every frame keeps its size and place whatever it holds; long content scrolls inside the frame, so nothing jumps while the
data changes. When the row is wide enough, the tab strips spread over the full row with the same space around every
label; when it is too narrow, they keep a tight fit and scroll with `◀` and `▶`. A terminal smaller than the tab in view
needs shows the size it has and the size it needs instead of the tab. A dialog dims the page behind it; with no dialog
open, the dim never takes the mouse.

### Lists, groups and search

Lists show their items in titled groups, `▾ ── Online (70) ───`. A title is a row like any other: Enter, the arrow keys
or a click fold and open its group, and a folded group shows `▸`. Groups start open.

Lists search while you type, after the text has stood still for half a second. During a search a group with no match is
hidden, a group with matches is open whether or not it was folded, and clearing the search brings back the folds from
before. The footer reads `show 1–50 in 150+`; the `+` means more items exist than were loaded, because the database
does not count them. The Sessions search only filters the open sessions (by name, id, address, client or kind) and never
reads history. Names of accounts are coloured as the osu! chat console does, by the highest privilege: cyan for an
owner or developer, red for a moderator, yellow for a supporter, light yellow for a player, grey for a restricted
account.

The Refresh button, `[↻ Refresh]` on the right of the frame border, reloads the tab and keeps the selected item. Only the
list tabs have it: Monitor and About have nothing to reload, Chat updates itself, and Settings reads its values whenever
it opens. `Ctrl+R` does the same from the keyboard. The log and the chats follow their newest line until you scroll up;
`Jump to End` appears then, and following starts again when you reach the end.

### The detail panel

A click on an item opens its detail under the list, above the key hints. Clicking an account anywhere (Users, Chat,
Lobby, Scores, Sessions) opens the detail of that account, except a member of a chat channel, which opens a direct message
with that member; clicking a channel opens it directly. The panel is a third of
the terminal high whatever it shows. The fields stand in two columns on the left and scroll when long; the actions are
buttons in a column of fixed width on the right. There is no Close button: `Esc` or `x` closes the panel. `Left`, `Right`, `Up` and `Down` move between the actions, and
a dialog opened from an action takes `Esc` alone: the detail behind it stays.

Buttons that lead to another tab come first and are named after it with an ellipsis (`User…`, `Sessions…`), actions that
change something follow, and the ones that end something (kick, close session) come last, in red. A button with an
ellipsis replaces the panel in place with the detail or list of the other tab, and the breadcrumb on the frame border
shows the way (`Users › peppy › Sessions`). `[← Back]` or `Esc` returns one step, and the steps can go as deep as the
operator likes. A result that arrives after the operator already went back or closed the panel is dropped. In a terminal
narrower than 100 columns the buttons fold into one `[Actions]` button that opens them in a dialog. An action that fails
reports it with a toast.

### Toasts

A toast tells how something ended. It stands at the bottom right, above the key hints, 56 columns by 4 rows (two lines of text), with `✓` in
green for success, `!` in yellow for a warning and `✗` in red for a failure; a longer text is cut with `…` after the second line. A success stays
5 seconds and a failure 10. A message that is already on screen is not shown twice: the toast shows `×2`, `×3` and so
on, and its time starts again. At most four toasts show; a click or `Esc` closes one.

### Settings, Monitor and the mouse wheel

Settings has five tabs: Server (General, Cors, Uploads), Accounts (Accounts, Locks, Sessions, Rate limits), Gameplay
(Beatmaps, Multiplayer, Bot), Dashboard (the colour theme, kept in `Data/tuisettings.json`) and Configuration. Sizes show in
KB, MB or GB (`100 MB`) and durations as `30m`, `12h` or `7d`, in the Settings tab and in the `settings` commands. Each settings category is a foldable group inside its tab. Configuration
lists the effective application configuration grouped by section, each value with where it comes from (file, environment
variable or command line), then the environment variables in one last group (sub-grouped by
the `DOTNET_` and `ASPNETCORE_` prefixes); values of secrets are hidden. A one-line search above the fields filters
inside the tab in view. Fields are grouped by the
object they belong to, with names relative to the group (`Lockout time` under `Login`, not `Login: lockout time`), and a
fixed frame explains the field in focus.

Monitor reports the server only, not the machine. It holds the whole page in one scrolling view: three graphs on the first
row (CPU, RAM, connections), the four connection graphs (Stable, Tourney, IRC, API) on the second, then a grid of
categories whose cells are all 8 rows high and scroll inside. Every graph shows its current values on its first lines and
its legend on the last one, inside the box; the measurements a graph shows (clients, polls per second, p50/p95/p99,
process CPU and memory) are not repeated in the grid. The API graph shows the total of every HTTP host, and the HTTP cell
of the grid keeps the requests per second and response times of each host. Choosing a metric shows its explanation in a fixed frame at the bottom. The exceptions of the last
minute are one metric: up to ten types, the most frequent first, equal types merged as `Type ×N`.

The mouse wheel accelerates: notches that follow each other quickly move more rows each, and a pause starts again from
one notch per step. Scroll bars can be dragged and clicked.

## Running a command

A command can be typed in the command box or given on the command line of the executable:

```text
Basil users create --username admin --password secret --admin
```

Both use the same parser, print the same output and return the same exit code.

An account can be named instead of numbered (`users alice set --country vn`). A mistyped command or option is refused
before anything starts, with exit code 2, and `--help` prints the commands and exits with 0. When the server cannot be
reached, a command that needs it says where it looked and how to start the server.

| The server is...  | What happens                                                                                           |
|-------------------|--------------------------------------------------------------------------------------------------------|
| running           | the command is forwarded over the local socket `Temp/basil.sock` and the output streams back           |
| not running       | only commands with **P = yes** run, directly against the storage; any other command fails with code 3  |

### The local socket

* A Unix domain socket at `Temp/basil.sock`, readable and writable by the account that runs the server only (mode `0600`).
  It also works on Windows 10 and later.
* It exists only while the server runs. On startup the server first tries to connect to an existing file: if something
  answers, another server is running and this one refuses to start with a clear error; only if the connection is
  refused is the leftover file of a crashed server deleted and replaced. A client treats a refused connection as "the
  server is not running".
* The path of a Unix socket is limited to about 108 bytes. When the folder is so deep that `basil.sock` would not fit,
  the socket is created instead in the user's runtime directory (`XDG_RUNTIME_DIR`, or the system temporary directory
  when it is not set), under a short name made from that folder; the server and the shell choose the same path. When no socket can be created the server logs a clear error and runs
  without it (shell commands then fall back to offline mode, which is unsafe while the server runs).
* The protocol is one request and one streamed reply: the client sends the argument list as one UTF-8 line (a JSON array
  of strings); the server runs it as the operator and streams the plain text output framed per line: every output line
  is sent as `o <text>` and the reply ends with `x <exit code>`, so output can never be mistaken for the exit code.
  The socket has no interactive questions: the confirmation of `shutdown` is asked in the calling terminal
  before the request is sent.
* The command runs on the machine of the server only. Calling from another machine or container is not supported; in
  Docker use `docker compose exec basil ./Basil <command>`.

### Running offline

With the server stopped, a command with P = yes starts only the storage: the directories are created, the database is
migrated (an empty database too), the stored data is loaded. The running state is not recovered and no listener is
opened. A command waits for its changes to be stored, so a command that printed its confirmation has stored its change.

> **Never run an offline command while another server uses the same database.** The offline process writes straight to
> the database, and the running server's live state may disagree with it.

### Exit codes of a command

| Code | Meaning                                                                                  |
|------|------------------------------------------------------------------------------------------|
| 0    | success                                                                                  |
| 1    | the operation was refused: not found, conflict, gone, locked, system account, forbidden  |
| 2    | the input is not valid, or the command line cannot be understood                         |
| 3    | the server is not running and the command needs it                                       |
| 4    | the operator declined the confirmation                                                   |

The refusal is printed as `error: <message>` in plain words; the exit code tells the kind of refusal. `privilegeDenied`
never happens, because the console skips the checks, and `locked` never happens either, because the console passes a lock
as a manager does.

### Exit codes of the process

| Code | Meaning                                                              |
|------|----------------------------------------------------------------------|
| 0    | the server shut down normally                                        |
| 1    | the server stopped because of an error                               |

Basil has no restart command: in Docker the restart policy (`on-failure`) restarts a container that exited with an error,
and `docker compose restart basil` restarts it on purpose.

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
* Values have the API's types. **A set of flags is written as names joined by commas** (`--privileges player,supporter`,
  `--types stable,irc`, or `none`), never as a number; the command box completes the names. An ordinary enum is its camelCase name; a boolean is `true` or `false`; a duration is `30m`, `12h` or `7d`.
* Every list takes `--offset` and `--limit` as the API does (default 0 and 25, at most 100).
* A route that returns a file or a stream (replays, beatmap files, avatars, the live `/live` and match streams) and the
  routes of one's own session (`/v1/auth/*`, `/v1/me`, `/v1/sessions`) have no
  command: the console is not signed in.

## Output

The output is for people and never JSON; there is no `--json` option.

* One object is a list of aligned `label: value` lines.
* A list is a table with a header row.
* A set of flags is shown as its names joined by commas: `privileges: player,supporter`; no number is shown.
* A refusal is one line, `error: <message>`, without an internal code.
* Times are shown in the operator's local time zone (injected, so the output of a test does not depend on the machine).
* The result of an action is one confirmation line.
* In the dashboard the output is coloured with the theme; from a shell, over the socket and with `--console 0` it is plain
  text without colour.

## Keys and mouse

Every mouse action has a keyboard equivalent, so the dashboard works over a plain SSH session. `Alt+Left` does what `Esc`
does everywhere, because some terminals delay or swallow `Esc`. A dialog takes the keys first; the tables below apply
when none is open.

### Global

| Key                         | Mouse                | Does                                                                                   |
|-----------------------------|----------------------|----------------------------------------------------------------------------------------|
| `F1` to `F10`, `Alt+1` to `Alt+0` | click a tab title | Switch tab                                                                             |
| `Ctrl+Left`, `Ctrl+Right`   |                      | Previous or next tab; inside a text box they move by word instead                      |
| `Tab`, `Shift+Tab`          | click a pane         | Next or previous pane (search, list, detail actions)                                   |
| `Ctrl+R`                    | click `[↻ Refresh]`  | Reload the tab in view (list tabs and Settings)                                        |
| `Ctrl+End`                  | click `Jump to End`  | Follow the newest line (Console, Chat)                                                 |
| `Esc` or `Alt+Left`         | click `[X]`          | Close the top thing: dialog, detail, search text, search box, and in a narrow Chat the channel view |
| `Ctrl+C`                    |                      | See [Modes](#modes)                                                                    |
|                             | wheel                | Scroll the pane under the pointer                                                      |

### Lists (Users, Beatmaps, Lobby, Scores, Chat channels and members)

A list tab opens with the list focused, not the search box.

| Key                                   | Mouse                                   | Does                                                       |
|---------------------------------------|-----------------------------------------|------------------------------------------------------------|
| `Up`, `Down`, `PageUp`, `PageDown`, `Home`, `End` | wheel                       | Move the selection                                         |
|                                       | click a row                             | Select the row                                             |
| `Enter`                               | click a row                             | Open the detail; on a group title, fold or open the group  |
| `/` or `Ctrl+F`                       | click the search box                    | Focus the search                                           |
| `Down` in the search                  |                                         | Leave the search and select the first item                 |
| `Up` on the first item                |                                         | Go to the search                                           |
| `Esc` in the search                   |                                         | Clear the text; with no text, leave the search             |
| `?` then `Enter` in the search        |                                         | Show the search syntax of the tab (`Esc`, `Enter` or `[X]` closes it) |
| `Esc` or `x` with a detail open       | `[← Back]` inside a nested detail       | Go back one step, or close the detail                      |
| `Left`, `Right`, `Tab` in a detail    | click an action                         | Move between actions; `Enter` runs the one in focus        |

### Settings

Two levels: the category strip, then the fields of the category.

| Key                                  | Mouse                      | Does                                                      |
|--------------------------------------|----------------------------|-----------------------------------------------------------|
| `Left`, `Right` on the strip         | click a category           | Change category                                           |
| `Down` or `Enter` on the strip       |                            | Enter the first field                                     |
| `Up`, `Down` in the fields           | click a field              | Move between fields; `Up` on the first returns to the strip |
| `Esc` or `Alt+Left` in the fields    |                            | Back to the strip, dropping an unsaved edit of the field  |
| `Enter`                              |                            | Save a text field                                         |
| `Space` or `Enter`                   | click a switch             | Toggle a switch                                           |

### Console

| Key                                  | Mouse                      | Does                                                      |
|--------------------------------------|----------------------------|-----------------------------------------------------------|
| typing                               |                            | Opens a completion popup above the input: up to 8 matching commands or arguments with a short description |
| `Up`, `Down`                         |                            | Move in the popup; with the popup closed, walk the history |
| `Tab`, or `Enter` on a highlighted item | click a popup row       | Accept the item                                           |
| `Enter` with no popup                |                            | Run the line                                              |
| `Esc`                                |                            | Close the popup; with none open, clear the line           |

### Chat

| Key                                  | Mouse                      | Does                                                      |
|--------------------------------------|----------------------------|-----------------------------------------------------------|
| `Tab`, `Shift+Tab`                   |                            | Channels, then the message box, then the members          |
| `Up`, `Down`, `Enter` on a channel   | click a channel            | Select it; `Enter` opens it and focuses the message box   |
| `Enter` in the message box           |                            | Send                                                      |
| `Enter` on a member                  | click a member             | Open a direct message with the member                     |
| `Ctrl+B`                             |                            | Show or hide the members                                  |
| `Ctrl+D`                             | click `[Channel]`          | Show the detail of the channel in view                    |
| `Esc` or `Alt+Left`                  |                            | In a narrow terminal, back to the channel list           |

The channel and member searches hide the sections that have no match.

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
| `theme ["<scheme>"]`         | Lists the colour schemes or changes the dashboard theme and saves it. Dashboard only. A name with spaces is written in quotes. | no  |

### users

| Command                                                                                             | API route                                                     | P   |
|-----------------------------------------------------------------------------------------------------|---------------------------------------------------------------|-----|
| `users list [--query <q>] [--offset] [--limit]`                                                     | `GET /v1/users`                                               | yes |
| `users create --username <name> --password <pw> [--country <cc>] [--privileges <names>] [--admin]`  | `POST /v1/users`                                              | yes |
| `users <id>`                                                                                        | `GET /v1/users/{id}`                                          | yes |
| `users <id> set [--username] [--country] [--away] [--pmPrivate] [--privileges <names>] [--password <pw>]` | `PATCH /v1/users/{id}`; `--password` is `PUT .../password` | yes |
| `users <id> avatar --file <path>`, `users <id> avatar delete`                                       | `PUT`, `DELETE /v1/users/{id}/avatar`                         | yes |
| `users <id> friends list`                              | `GET /v1/users/{id}/friends`                              | yes |
| `users <id> blocks list`                                 | `GET /v1/users/{id}/blocks`                               | yes |
| `users <id> silence --duration <d> [--reason <text>]`                                               | `PUT /v1/users/{id}/silence`                                  | yes |
| `users <id> unsilence`                                                                              | `DELETE /v1/users/{id}/silence`                               | yes |
| `users <id> sessions list [--state all\|online\|offline] [--types <names>]`                          | `GET /v1/users/{id}/sessions`                                 | yes |
| `users <id> sessions <sid>`                                                                         | `GET /v1/users/{id}/sessions/{sid}`                           | yes |
| `users <id> sessions <sid> connections`                                                             | `GET /v1/users/{id}/sessions/{sid}/connections`               | yes |
| `users <id> sessions <sid> delete`                                                                  | `DELETE /v1/users/{id}/sessions/{sid}`                        | yes |
| `users <id> sessions delete`                                                                        | `DELETE /v1/users/{id}/sessions` (none is kept)               | yes |
| `users <id> delete`                                                                                 | `DELETE /v1/users/{id}`                                       | yes |

`--password` is the plain password; the server hashes it as it does for the API. `--admin` is a convenience that gives the account every privilege (deployment scripts use it to create the first administrator); `-u` and `-p` are short names of `--username` and `--password`. `--duration` replaces the API's
`endsAt`: the silence ends that long from now. `--password` and `--new` are masked
in the log, in the history and in suggestions.

### channels

| Command                                                                                                           | API route                     | P   |
|-------------------------------------------------------------------------------------------------------------------|-------------------------------|-----|
| `channels list`                                                                                                   | `GET /v1/channels`            | yes |
| `channels create --name <#name> [--topic] [--readPrivileges <names>] [--writePrivileges <names>] [--autoJoin] [--visible]` | `POST /v1/channels`     | yes |
| `channels <name> set [--topic] [--readPrivileges <names>] [--writePrivileges <names>] [--autoJoin] [--visible]`      | `PATCH /v1/channels/{name}`   | yes |
| `channels <name> delete`                                                                                          | `DELETE /v1/channels/{name}`  | yes |

### rooms

Rooms exist only while the server runs. The room search syntax: free text matches the name; `host:`, `creator:` and
`player:` take an account name or id; `status:open|locked|playing`; `mode:0` to `3`; `password:yes|no`.

| Command                                                          | API route                          | P  |
|------------------------------------------------------------------|------------------------------------|----|
| `rooms list [--query <q>] [--offset] [--limit]`                  | `GET /v1/rooms`                    | no |
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
| `beatmapsets reload`                                                             | `POST /v1/beatmapsets/reload`       | no  |
| `beatmaps <id>`                                                                  | `GET /v1/beatmaps/{id}`             | yes |

`beatmapsets reload` recomputes the metadata of every stored beatmap, star rating included. It runs in the background,
one at a time, and logs its progress. Run it after a new image brings a new osu! version, because the star ratings stored
before are those of the old one. Changing a set's status can change how clients submit scores and replays; prefer a status that has a
leaderboard (ranked, approved, qualified, loved).

### version and rulesets

| Command          | API route                            | P   |
|------------------|--------------------------------------|-----|
| `version`        | `GET /v1/version`                    | yes |
| `ruleset version`| `GET /v1/settings/ruleset/version`   | yes |
| `ruleset update` | `POST /v1/settings/ruleset/update`   | yes |

Star ratings, pp and beatmapset imports need the osu! calculation stack: the osu! libraries the server was built
against, kept in `Data/Rulesets/<version>/`. The image does not carry them. `ruleset version` shows the version the
build needs and the state: `Installed`, `Missing` or `Damaged`. Without the stack the server runs, star ratings and pp
stay empty and imports are refused; a `.osz` left in `Data/Beatmapsets/` waits there.

`ruleset update` downloads the stack from the NuGet feed, checks every package and file against the hashes the build
carries, and keeps nothing when a check fails; the answer names the package. On a first install the stack is used at
once: waiting `.osz` files are imported, missing star ratings are calculated in the background, and `scores recalculate`
fills the pp of stored scores. When the stack was damaged while the server ran, restart the server after the update.
The files are checked at every start; a damaged file turns the stack off without stopping the server.

### scores

| Command                                                          | API route               | P   |
|------------------------------------------------------------------|-------------------------|-----|
| `scores list [--beatmap <id>] [--user <id>] [--offset] [--limit]` | `GET /v1/scores`       | yes |
| `scores <id>`                                                    | `GET /v1/scores/{id}`   | yes |
| `scores recalculate [--all]`                                     | `POST /v1/scores/performance/recalculate` | no |

`scores recalculate` calculates the pp of the passed scores that have none (every passed score with `--all`) in the
background, one run at a time, and logs its progress. A score whose beatmap or difficulty file is missing keeps the pp it
has. It needs `Developer` and runs by itself at startup for the scores without pp.

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
console; everywhere else a set of flags is a list of names.

### diagnostics

There is no command that shows diagnostics; the dashboard shows them. There are the actions only.

| Command                      | API route                                    | P   |
|------------------------------|----------------------------------------------|-----|
| `diagnostics gc collect`     | `POST /v1/diagnostics/gc/collect`            | no  |
| `diagnostics caches clear`   | `POST /v1/diagnostics/caches/clear`          | yes |
| `diagnostics exceptions reset` | `POST /v1/diagnostics/exceptions/reset`    | no  |

## Shutdown

`shutdown` has no HTTP route and no chat command. It runs the graceful shutdown of the server. Typed in
the dashboard without `--now` they ask `y/N` in the command box. Given on the command line of a running server without
`--now` the question is asked in the calling terminal before anything is sent; declining ends with code 4.

## Design and invariants

* Commands run as BasilBot with the operator's connection, which skips the privilege checks; this exists only on the
  console path. BasilBot's own connections and the chat commands keep their normal limits.
* Every command is built from the same Application behaviours as the HTTP route it mirrors, and prints the same record
  view. Every route has a command, except the ones listed under the mapping rule.
* A password, a new password or a key never appears in the log, the history, the suggestions or the output.

## Related code

* `Basil.Host.Cli` - the dashboard, the command parser, the runner and the socket.
* `Basil.Host.Shared` - the record views and the outcome table both HTTP and console use.
* [`configuration.md`](configuration.md) - the folders beside the executable, including `Temp/` where the socket lives.
