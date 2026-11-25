# The edge: nginx in front of Basil

## Overview

Basil never faces the network directly. In a deployment, nginx stands in front of it and Basil serves plain HTTP and plain
IRC inside the Compose network. This page is the authoritative description of who does what between the client and the
Application: which rule belongs to nginx, which to the composition root, the Gateway and the hosts, and which to the
business. The operator's view of the same stack is in [`docker.md`](../for-technicians/docker.md) and
[`https.md`](../for-technicians/https.md); the layout of `Data/` and the settings are in
[`configuration.md`](../for-technicians/configuration.md).

## Why

Each layer should do only what the layers inside it cannot know, so that the Application deals with the business alone.
nginx sees the network: addresses, TLS, the size a body declares, how fast a peer sends. It is a better place for those
rules than application code, which would repeat them per route and per protocol. Basil sees the user: who signed in, what
they may do and how much server capacity their kind of work may take. Keeping the two apart means Basil holds no
certificate, no per-address rule and no code that only exists to guard the process from the Internet, and an operator can
tune the edge without touching the server.

## The roles

| Layer | Does | Never does |
|---|---|---|
| Cloudflare (optional, HTTP only) | DDoS and WAF protection, hides the server's address, caches images by their `Cache-Control`; the osu! subdomains pass without a challenge | carry IRC; anything a LAN needs |
| nginx | TLS for every subdomain and for IRC on `6697`; the real client address (`real_ip_header`, Cloudflare's ranges when used); per-address `limit_req` and `limit_conn` for HTTP and IRC; body size per upload route, refused from the declared length; header and body timeouts; serves stored files with ranges; no buffering on streams; upstream keep-alive; HSTS | judge a user, a privilege or the content of a body |
| Composition root `Basil` | Kestrel on plain HTTP and the IRC listener on plain TCP; trusts only the nginx peer for the client address, country and scheme; routes by subdomain to the hosts; `MaxRequestBodySize` as a backstop | admission policy, business rules |
| Gateway | Allow or Deny for a signed-in user (allowance per work class, uploads per user, chat and mutation rules, the sign-in guard) and for server capacity (concurrency per work class, shedding by load, `/health`); see [`gateway.md`](gateway.md) | per-address rules, user types, privileges |
| Hosts and `Host.Shared` | translate the wire into messages and back; authenticate through Sessions; map the `Session` to an `AdmissionSubject`; check what the client sent; hand a stored file to nginx; wrap an unread upload body | business, permission or state rules |
| Protocol | bytes and delimited strings to DTOs and back | validate or give meaning |
| Application, Domain, Infrastructure | the business: privileges through `ScopeTo`, rules, outcomes, events; ports check content; storages write and move files | transport, addresses, TLS, rate rules |

## How a request travels

```text
client ──TLS──► nginx ──HTTP / IRC + PROXY header──► Basil root ──► host ◄──► Gateway
                  │                                                  │
                  └── reads Data/ and Cache/ read-only               └──► Mediator ──► Application
```

An API call crosses nginx (TLS, per-address limits), the root (trusted peer, subdomain), the Rest host (token to
`Session`, then the Gateway), becomes a message and reaches the Application, and the outcome returns as an envelope. An
osu! poll keeps its connection alive through nginx; the Stable host authenticates the `osu-token` and applies the poll
allowance. An IRC line crosses nginx's `stream` block, which ends TLS and sends the PROXY header; the Irc host reads it,
parses the line with Protocol, asks the Gateway and sends the message.

### The client address

nginx sets `X-Real-IP` to the peer address (or, behind Cloudflare, to `CF-Connecting-IP` for requests from Cloudflare's
ranges), `X-Forwarded-Proto` to the scheme and the country header to the country Cloudflare reports. Basil believes these
only from an address in `Basil:Server:TrustedProxies` and removes them from every other request; so a client that reaches
Basil by a path that skips nginx cannot name its own address. The Compose file trusts the Compose network.

IRC carries the address in a PROXY protocol v1 header that nginx writes first. Basil reads it only from a trusted peer
and closes without a reply every connection that does not start with a valid header within five seconds, so no plaintext
path from outside reaches IRC even if its port is published by mistake. IRC is therefore TLS only, on `6697`; a tool that
speaks only plain IRC cannot connect. See [`irc.md`](irc.md).

### Stored files

A stored file (an image, an `.osz`, a beatmap file, audio, a video, a replay) is served by nginx. Basil still looks the
file up and checks the right to read it, then answers with the headers and an `X-Accel-Redirect` that names an `internal`
location; nginx streams the bytes with ranges from the `Data/` and `Cache/` volumes, which it mounts read-only. A file that
is not on the volume, such as a default that ships with the program, is served by Basil. When
`Basil:Server:Files:AccelPrefix` is empty (a development run without nginx) Basil serves every file itself. There is no
proxy cache, and a large payload never passes through a cache.

### Uploads

An upload is judged before its bytes are taken. In order:

1. nginx refuses a declared `Content-Length` above the route's ceiling, and times out a slow body.
2. The host authenticates the caller and asks the Gateway; the owning Application object authorizes the action before the
   body is read (an early check, repeated by the operation because a privilege can change during the upload).
3. The host wraps the unread body in a stream with a byte counter, the declared length and a minimum data rate, and hands it
   to the operation. A mismatch, too many bytes or a slow sender cuts the connection.
4. The port checks the first bytes (image signature and dimensions, zip header), the storage writes them once into its own
   temporary file, the port checks the whole, and the storage moves the file into place atomically.

A refused upload is only logged (caller, address, route, reason, declared size); nothing is kept. The osu! client's own
uploads (screenshot, score with replay) carry their credentials in the form and cannot change, so they are accepted only
from an address that has an open osu! session, and the form credentials are still checked.

### Streams

A live stream turns proxy buffering off (`X-Accel-Buffering: no`) and sends a heartbeat below nginx's and Cloudflare's idle
timeouts; nginx keeps `proxy_timeout` for IRC above IRC's 300-second idle limit. See [`sse.md`](sse.md).

## Invariants

* Basil terminates no TLS and holds no certificate; every certificate, the default one included, lives with nginx.
* Every per-address limit is nginx's. Basil's limits are per signed-in user, per account, or for server capacity.
* Basil trusts a forwarded address, country or scheme only from `Basil:Server:TrustedProxies`.
* Only nginx's ports are published; Basil's HTTP port and its IRC port are reachable inside the Compose network only.
* An upload's content is checked by the owning module from one stream, and no part of an uploaded name becomes a path.

## Related code

* [`docker/nginx/`](../../docker/nginx/): the nginx configuration and the default certificate
* [`docker-compose.yml`](../../docker-compose.yml): the stack and the settings it passes to Basil
* [`TrustedProxiesMiddleware.cs`](../../Basil/Http/TrustedProxiesMiddleware.cs): trusting the nginx peer
* [`ClientAddress.cs`](../../Basil.Host.Shared/Http/ClientAddress.cs): the client address and country
* [`StoredFile.cs`](../../Basil.Host.Shared/Http/StoredFile.cs): handing a stored file to nginx
* [`UploadRequest.cs`](../../Basil.Host.Shared/Http/UploadRequest.cs), [`UploadForm.cs`](../../Basil.Host.Shared/Http/UploadForm.cs): the guarded upload read
* [`IrcListener.cs`](../../Basil.Host.Irc/Transport/IrcListener.cs): the PROXY header on IRC

## See also

* [`gateway.md`](gateway.md): admission control behind the edge
* [`architecture.md`](architecture.md): where the hosts and the composition root sit
* [`docker.md`](../for-technicians/docker.md): running the stack
* [`https.md`](../for-technicians/https.md): certificates, Cloudflare and trusting the proxy
