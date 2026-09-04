# Troubleshooting

This page covers the problems most commonly encountered when operating Basil.

When troubleshooting, **check the logs first**. See [`logging.md`](logging.md) for log locations and how to increase logging detail.

For most problems, work through the checks below in order.

---

## Basil does not start

Check the startup log first.

Common causes include:

* an invalid configuration value;
* an invalid TLS certificate or certificate password;
* the configured port is already in use;
* the process does not have permission to access required files or bind to the configured port.

If Basil exits immediately after a configuration change, check the most recent entries in:

```text
Logs/latest.log
```

For Docker:

```bash
docker compose logs --no-color
```

---

## Basil exits immediately with a TLS error

If Basil exits during startup after changing the certificate configuration, check:

```text
Basil:Server:CertPath
Basil:Server:CertPassword
```

Verify that:

1. The PFX file exists.
2. The path is correct.
3. The Basil process can read the file.
4. The PFX password is correct.
5. The PFX contains the certificate's private key.

Basil logs the certificate path when this fails, but **never logs the certificate password**.

See [`https.md`](https.md).

---

## Audio preview returns `503 Service Unavailable`

Basil requires `ffmpeg` to generate beatmap audio previews.

Check that `ffmpeg` is installed and available to the Basil process:

```bash
ffmpeg -version
```

Install it if necessary, then restart Basil.

Nothing else on the server requires `ffmpeg`, so the rest of the server can continue operating without it.

### Docker

The official Basil Docker image already includes `ffmpeg`.

If audio previews fail in Docker, check the container logs instead of installing `ffmpeg` on the host:

```bash
docker compose logs -f
```

---

## Beatmap download reports that the beatmap is unavailable

First check whether the beatmapset exists in:

```text
Data/Beatmapsets/
```

Basil supports both local storage and optional external mirrors.

### Local-only mode

If `GET /settings/mirror`'s `downloadEndpoint` is unset, Basil uses local storage only.

Check that:

* the `.osz` was added to `Data/Beatmapsets/`;
* Basil had time to ingest it;
* the beatmapset appears after restarting Basil if it was added while the server was offline.

Basil watches the beatmapset directory while running and also performs a full reconciliation during startup.

### Mirror mode

If `downloadEndpoint` is set, Basil can redirect requests for missing beatmapsets to the configured mirror.

If you do not want external downloads, clear it with `PUT /settings/mirror` — no restart required. See
[`configuration.md`](configuration.md#beatmap-mirror).

---

## Basil reports that it is running in bypass mode

A fresh database has no admin key.

In this state, Basil runs in **bypass mode**:

* admin-protected API operations do not require a key;
* in-game registration does not require the admin key.

This is expected for a new installation, but it is unsafe for an exposed server.

Set an admin key through:

```text
PUT /settings/adminkey
```

on the `api.<domain>` host.

The new key takes effect immediately. A restart is not required.

See [`configuration.md`](configuration.md).

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
osu! -devserver basil.local
````

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
```

For a self-signed certificate, make sure the certificate is trusted on the client machine.

A certificate for only `localhost` or only the main domain is not sufficient.

See [`https.md`](https.md).

### 3. Check DNS or hosts entries

From the client machine, verify that every required Basil hostname resolves to the server.

A single missing hostname can prevent the osu! client from connecting correctly.

For LAN deployments, check the client's hosts file.

See [`getting-started.md`](../for-client/bancho/getting-started.md).

### 4. Check port 443

Make sure TCP port `443` is reachable from the client.

Check:

* the Basil server's firewall;
* any network firewall between the client and server;
* the configured `Basil:Server:Port`.

### 5. Check Basil's connectivity endpoint

From the client or another machine that can reach the server:

```bash
curl -k https://osu.<domain>/web/bancho_connect.php
```

A working server should return:

```text
200 OK
```

If this fails, fix the server/network problem before troubleshooting the osu! client.

---

## IRC connection is refused

Basil IRC uses a separate TCP port.

The default is:

```text
Basil:Irc:Port = 6667
```

Check:

1. Basil is running.
2. The configured IRC port is correct.
3. The port is allowed through the server firewall.
4. The client is connecting to the correct hostname or IP.
5. No other service is already using the configured port.

IRC does not use HTTPS or the Basil TLS certificate.

There is also no `irc.<domain>` hostname requirement.

---

## IRC client connects but cannot join `#mp_x`

Make sure you use `/join #mp_x` instead of `/chat #mp_x`

Only users who are allowed to participate in the multiplayer match can join its room channel.

If a user receives:

```text
Cannot join channel (no permission)
```

the user is not currently permitted to join that match channel.

This is different from:

```text
No such channel
```

which indicates that the channel does not exist.

See [`irc.md`](../for-developers/irc.md) for multiplayer IRC behaviour.

---

## In-game registration fails with a key error

In-game registration uses the Basil admin key as the **Email** value in osu!'s registration form.

Check that:

1. An admin key has been configured.
2. The key entered in the Email field matches the current key.
3. The client is connecting to the correct Basil server.

If the server is in bypass mode because no admin key has been configured, the registration key is not required.

See [`configuration.md`](configuration.md).

---

## Client repeatedly shows `Unknown token, reconnecting`

Basil session state is stored in memory.

A server restart therefore invalidates existing client sessions. Clients reconnecting after a restart is expected behaviour.

If clients repeatedly reconnect **without a server restart**, check:

* Basil logs;
* reverse proxy configuration, if one is being used;
* network connectivity;
* whether the proxy or firewall is terminating long-lived client requests.

If the problem affects only IRC clients, troubleshoot the IRC TCP connection separately.

---

## Changes to `appsettings.json` have no effect

Basil reads configuration when the process starts.

After changing [`appsettings.json`](../../src/Basil.Web/Data/appsettings.json):

1. Save the file.
2. Stop Basil.
3. Start Basil again.

No rebuild is required.

For Docker:

```bash
docker compose restart
```

Basil does not support overriding its application settings through environment variables. Edit `appsettings.json` instead.

See [`configuration.md`](configuration.md).

---

## Data or beatmaps appear to be missing

Check that Basil is using the expected `Data/` directory.

The persistent data directory is located next to the executable in a normal deployment.

For Docker, check the host directory mounted to:

```text
/app/Data
```

The database is:

```text
Data/Basil.db
```

Do not replace or delete this file unless you intentionally want to create a new Basil installation.

For beatmaps, check:

```text
Data/Beatmapsets/
```

For other data locations, see [`configuration.md`](configuration.md).

---

## Logs are missing

Check:

```text
Logs/
```

next to the Basil executable.

For Docker, check:

```bash
docker compose logs -f
```

If persistent Docker logs are required, make sure `Logs/` is bind-mounted to the host.

See [`logging.md`](logging.md).

---

## Need more diagnostic information

Temporarily increase the log level:

```json
{
  "Basil": {
    "Logging": {
      "MinimumLevel": "Debug"
    }
  }
}
```

Restart Basil, reproduce the problem, and collect the relevant logs.

After troubleshooting, return the setting to:

```text
Information
```

to avoid unnecessary log volume.

See [`logging.md`](logging.md).

---

## See also

* [`logging.md`](logging.md): log locations, log levels, and collecting logs
* [`deployment.md`](deployment.md): complete deployment procedure
* [`configuration.md`](configuration.md): configuration and persistent data
* [`https.md`](https.md): TLS and certificate requirements
* [`docker.md`](docker.md): Docker deployment
* [`getting-started.md`](../for-client/bancho/getting-started.md): configuring an osu! client
