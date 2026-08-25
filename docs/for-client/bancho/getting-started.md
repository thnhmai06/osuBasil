# Connecting an osu! stable client

## Overview

This guide explains how to connect an **osu! stable** client to a Basil server.

The procedure is the same for local development, a LAN server, or a publicly deployed server. The client needs:

* the Basil server's domain;
* a trusted TLS certificate;
* an account;
* the `-devserver` launch option.

For server-side setup, see [`deployment.md`](../../for-technicians/deployment.md).

---

## 1. Use osu! stable

Basil supports the classic **osu! stable** client.

The client must be launched with the `-devserver` option to connect to a Basil server instead of the official osu! servers.

---

## 2. Trust the server certificate

The client must trust the TLS certificate used by the Basil server.

### Public server

If the server uses a certificate issued by a publicly trusted CA, such as an ACME certificate, no additional certificate installation is required.

### Local or LAN server

If the server uses a self-signed certificate, install the certificate on every machine running the osu! client.

* **Windows:** open `certmgr.msc`, then import the certificate into **Trusted Root Certification Authorities**.
* **macOS:** open **Keychain Access**, import the certificate into the appropriate keychain, then mark it as **Always Trust**.
* **Linux:** install the certificate into the distribution's trusted CA store.

The certificate must cover the Basil domain and all required subdomains.

See [`https.md`](../../for-technicians/https.md) for the server certificate requirements.

---

## 3. Make the Basil domain resolve

The client must be able to resolve all Basil subdomains to the server.

For a public deployment, configure normal DNS records.

For a LAN or local deployment, add entries to the client's hosts file.

On Windows:

```text
C:\Windows\System32\drivers\etc\hosts
```

On macOS/Linux:

```text
/etc/hosts
```

For example:

```text
<server-ip> basil.local
<server-ip> c.basil.local
<server-ip> ce.basil.local
<server-ip> c4.basil.local
<server-ip> c5.basil.local
<server-ip> c6.basil.local
<server-ip> osu.basil.local
<server-ip> b.basil.local
<server-ip> a.basil.local
<server-ip> api.basil.local
<server-ip> assets.basil.local
```

Replace `basil.local` with the value configured in `Basil:Server:Domain`.

If the client and server run on the same machine, `<server-ip>` can be `127.0.0.1`.

All entries must point to the same Basil server.

---

## 4. Get an account

There are two ways to create an account.

### In-game registration

1. Launch osu! stable with `-devserver`.
2. Click **Register** on the login screen.
3. Enter the Basil server's **admin key** in the **Email** field.
4. Choose a username and password.
5. Complete registration.

See [`configuration.md`](../../for-technicians/configuration.md) for information about the admin key.

### Admin API

An administrator can also create an account through Basil's admin API.

See the API documentation available at:

```text
https://api.<domain>/docs/
```

A newly created account is automatically granted the `Verified` privilege after its first successful login.

---

## 5. Launch osu! stable

Start the client with:

```text
osu!.exe -devserver <domain>
```

For example:

```text
osu!.exe -devserver basil.local
```

Replace `<domain>` with the exact value configured in:

```text
Basil:Server:Domain
```

Then log in using the Basil account.

---

## Troubleshooting

If the client cannot connect, check these in order:

1. The client is **osu! stable**, not osu! lazer.
2. The `-devserver` value matches `Basil:Server:Domain`.
3. The server certificate is trusted by the client machine.
4. The certificate covers all required Basil subdomains.
5. All required subdomains resolve to the Basil server.
6. TCP port `443` is reachable from the client.
7. The Basil server is running.

From the client machine, you can verify the server's basic connectivity with:

```bash
curl -k https://osu.<domain>/web/bancho_connect.php
```

A working server should return HTTP `200`.

See [`troubleshooting.md`](../../for-technicians/troubleshooting.md) for common server-side problems.

---

## See also

* [`authentication.md`](authentication.md): how osu! stable authenticates with Basil
* [`protocol.md`](protocol.md): the protocol used after login
* [`overview.md`](../api/overview.md): Basil's HTTP API
* [`irc-client.md`](../irc-client.md): connecting an IRC client
* [`basil-bot.md`](../basil-bot.md): BasilBot chat commands
* [`working-scopes.md`](../../for-developers/working-scopes.md): how Basil compares to official osu!Bancho
* [`deployment.md`](../../for-technicians/deployment.md): deploying a Basil server
* [`https.md`](../../for-technicians/https.md): TLS and certificate requirements
