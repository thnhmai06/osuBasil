# Troubleshooting

This page covers the problems most commonly encountered when operating Basil.

When troubleshooting, **check the logs first**. See [`logging.md`](logging.md) for log locations and how to increase logging detail.

For most problems, work through the checks below in order.

---

## Basil does not start

Check the startup log first.

Common causes include:

* an invalid configuration value;
* `ConnectionStrings__Write` is not set (the server stops with one line that names the variable);
* the PostgreSQL database named by `ConnectionStrings__Write` (or `Read`) cannot be reached or refuses the role;
* the configured port is already in use;
* `Data/` has a layout newer than this Basil knows, or the database holds a migration this Basil does not know: an older
  image was started on the data of a newer one (the line names the module and the migration); start the newer image
  again, or restore the backup;
* the process cannot write `Data/`, `Cache/` or `Logs/` (in Docker, give them to user `1654`; see [`docker.md`](docker.md)).

If Basil exits immediately after a configuration change, check the most recent entries of the log file of the day:

```text
docker-data/Logs/basil-YYYYMMDD.log
```

The `basil` container owns a terminal for the dashboard, so `docker compose logs basil` shows the dashboard, not the
log. For nginx and PostgreSQL, `docker compose logs nginx postgres` is the log.

---

## nginx does not start after changing the certificate

Basil holds no certificate, so a certificate problem stops nginx, not Basil. Verify that:

1. The two mounted files exist and nginx can read them.
2. The key matches the certificate and is not password protected.
3. `docker compose logs nginx` names the error.

See [`https.md`](https.md).

---

## Audio preview returns `404 Not Found`

Basil requires `ffmpeg` to build beatmap audio previews. A preview that could not be built does not exist, so its
request answers `404`; the failure is a warning in the system log, and the rest of the server keeps working.

The Basil image includes `ffmpeg`. Check the system log in `docker-data/Logs/` for the warning "Audio preview build
failed", which names the beatmapset and the audio file.

---

## Beatmap download reports that the beatmap is unavailable

First check whether the beatmapset exists in:

```text
Data/Beatmapsets/
```

Basil serves beatmaps only from local storage, so a beatmapset the server does not hold answers 404.

Check that:

* the `.osz` was added to `Data/Beatmapsets/`;
* Basil had time to ingest it;
* the beatmapset appears after restarting Basil if it was added while the server was offline.

Basil watches the beatmapset directory while running and also performs a full reconciliation during startup.

---

## Nobody can sign in, or no account can manage the server

The server never creates an account by itself, so a fresh database has no administrator. Create one with the console,
which works on the storage whether the server is running or not:

```bash
./Basil users create --username admin --password change-me --admin
```

`--admin` gives the account every privilege. If an existing administrator lost their password, set a new one with
`users <id> set --password <new>` (every session of that account is closed). See [`console.md`](console.md).

---

## osu! client cannot connect

Check the following in order.

### 1. Make sure the client is connecting to the correct Basil domain

Check that the osu! client is configured to connect to the same domain configured in:

```text
Basil:Server:Domain
```

For example, if the server is configured with:

```text
Basil:Server:Domain = basil.local
```

the client must connect to `basil.local` and its corresponding Basil subdomains.

```shell
osu!.exe -devserver basil.local
```

If the client is configured to use a different domain, it may fail to connect even when Basil itself is running correctly.

### 2. Check the TLS certificate

The certificate must cover the exact hostnames used by the client.

Basil requires the certificate to cover:

```text
<domain>
c.<domain>
ce.<domain>
c4.<domain>
c5.<domain>
c6.<domain>
osu.<domain>
b.<domain>
a.<domain>
api.<domain>
assets.<domain>
irc.<domain>
```

For a self-signed certificate, make sure the certificate is trusted on the client machine.

A certificate for only `localhost` or only the main domain is not sufficient.

See [`https.md`](https.md).

### 3. Check DNS or hosts entries

From the client machine, verify that every required Basil hostname resolves to the server.

A single missing hostname can prevent the osu! client from connecting correctly.

For LAN deployments, check the client's hosts file.

See [`getting-started.md`](../for-client/osu-stable/getting-started.md).

### 4. Check port 443

Make sure TCP port `443` is reachable from the client. nginx publishes it; Basil's own port `8080` is not published.

Check:

* the Basil server's firewall;
* any network firewall between the client and server;
* `docker compose ps`: `nginx` is running and lists `0.0.0.0:443->443`.

### 5. Check that the server answers

From the client or another machine that can reach the server:

```bash
curl -k https://api.<domain>/v1/users/1
```

A working server should return `200 OK` with the account of BasilBot.

If this fails, fix the server/network problem before troubleshooting the osu! client.

---

## IRC connection is refused

IRC is TLS only, on port `6697`, ended by nginx. Basil's own IRC port (`6667`) is not published and accepts only
connections that nginx opens with a PROXY protocol header, so a client cannot reach it directly.

Check:

1. The client connects to `irc.<domain>` on port `6697` with TLS on. A client or tool that speaks only plaintext IRC
   cannot connect.
2. `irc.<domain>` resolves to the server (behind Cloudflare, that record must be DNS only).
3. The port is allowed through the server firewall.
4. The client trusts the certificate (see [`https.md`](https.md)).
5. nginx is running (`docker compose ps`), and Basil is healthy.

A connection that nginx accepts and Basil closes at once means Basil did not receive the PROXY header from a trusted
address: check `Basil:Server:TrustedProxies` (the Compose file sets it to the Compose network).

---

## IRC client connects but cannot join `#mp_x`

Make sure you use `/join #mp_x` instead of `/chat #mp_x`

Only users who are allowed to participate in the multiplayer match can join its room channel.

If a user receives a `473` reply, the text names the reason, for example:

```text
Cannot join channel (you may not read it)
```

the user is not currently permitted to join that match channel.

This is different from:

```text
No such channel
```

which indicates that the channel does not exist.

See [`irc.md`](../for-developers/irc.md) for multiplayer IRC behaviour.

---

## In-game registration is refused

The registration form of the osu! client sends the username and the password; the email field is not used. A
registration is refused when:

1. The username is not valid or is already taken.
2. The **accounts** lock is on: while it is, only an account manager can create accounts (`GET /v1/settings/locks`).
3. The registration limit of the whole server is reached (`rateLimits` settings), or nginx refused the address for
   registering too often (`429`).

Create the account with the console instead if it must exist now: `users create --username <name> --password <word>`.
See [`configuration.md`](configuration.md).

---

## Client repeatedly shows `Unknown token, reconnecting`

osu! and IRC sessions end with the server: after a restart the osu! client is told its token is unknown and signs in
again with its saved password, and IRC clients sign in again with theirs. API sessions survive a restart for up to 8
hours of silence. This is expected behaviour.

If clients repeatedly reconnect **without a server restart**, check:

* Basil logs;
* reverse proxy configuration, if one is being used;
* network connectivity;
* whether the proxy or firewall is terminating long-lived client requests.

If the problem affects only IRC clients, troubleshoot the IRC TCP connection separately.

---

## Changes to `appsettings.json` have no effect

Basil reads most deployment configuration when the process starts; only `Logging:LogLevel` and `Basil:Updates` apply
while it runs.
Policy settings (locks, rate limits, upload limits and the like) are stored in the database and apply at once when
changed through the API or the console.

After changing `Data/appsettings.json` (`docker-data/Data/appsettings.json` in the Compose deployment; the shipped
copy is [`Basil/Data/appsettings.json`](../../Basil/Data/appsettings.json)):

1. Save the file.
2. Restart Basil with `docker compose restart basil`.

No rebuild is required.

Basil also reads settings from environment variables (`Basil__Server__Domain`, where a double underscore stands for
`:`) and from command-line arguments (`--Basil:Server:Domain=...`). Both win over `appsettings.json`; edit the file
instead when you want a persistent change.

See [`configuration.md`](configuration.md).

---

## Data or beatmaps appear to be missing

Check that Basil is using the expected `Data/` directory.

Check the host directory mounted to:

```text
/app/Data
```

in the `basil` container (`docker-data/Data` in the Compose file), and that nginx mounts the same folder.

The database is the PostgreSQL server configured by `ConnectionStrings:Write`. Basil creates its tables and applies
migrations on startup.

Do not drop or replace that database unless you intentionally want to create a new Basil installation.

For beatmaps, check:

```text
Data/Beatmapsets/
```

For other data locations, see [`configuration.md`](configuration.md).

---

## Logs are missing

Check:

```text
docker-data/Logs/
```

on the host, which the Compose file mounts to `/app/Logs`. `docker compose logs basil` is not a substitute: the
container owns a terminal for the dashboard.

See [`logging.md`](logging.md).

---

## Need more diagnostic information

Temporarily increase the log level:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  }
}
```

The change applies while the server runs. Reproduce the problem and collect the relevant logs.

After troubleshooting, return the setting to:

```text
Information
```

to avoid unnecessary log volume.

See [`logging.md`](logging.md).

---

## See also

* [`logging.md`](logging.md): log locations, log levels, and collecting logs
* [`deployment.md`](deployment.md): the steps from an empty machine to a running server
* [`configuration.md`](configuration.md): configuration and persistent data
* [`https.md`](https.md): TLS and certificate requirements
* [`docker.md`](docker.md): Docker deployment
* [`getting-started.md`](../for-client/osu-stable/getting-started.md): configuring an osu! client
