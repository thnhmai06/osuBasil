# The `api.` host HTTP surface

## Overview

The `api.<domain>` host provides HTTP endpoints used by clients and tournament tooling.

It provides:

* client-facing API endpoints;
* match reports and live match updates;
* file downloads;
* tournament and match management;
* user, beatmapset, FAQ, seasonal background, and menu icon management.

Most JSON endpoints use the standard Basil [response envelope](response-envelope.md).

File downloads and live SSE connections are exceptions and return their own response formats. See [`sse.md`](sse.md).

---

## API reference

The complete API reference is generated from Basil's OpenAPI definitions.

It contains:

* endpoint paths;
* HTTP methods;
* path and query parameters;
* request bodies;
* response schemas;
* status codes;
* authentication requirements.

**Do not rely on this page for the complete endpoint list.** Use the generated OpenAPI documentation instead.

### Running server

Open:

```text
https://api.<domain>/docs/
```

For example:

```text
https://api.example.com/docs/
```

### Online documentation

The API documentation is also available without running a Basil server:

[Basil API documentation](https://thnhmai06.github.io/osuBasil/)

The documentation site contains separate references for:

* **osu! client protocol**: endpoints used by the osu! client;
* **Basil API**: tournament and management API;
* **BasilBot**: chat commands;
* **IRC client**: IRC commands and behaviour.

---

## Authentication

Management endpoints require the Basil **admin key**.

Send the key using:

```http
Authorization: Bearer <key>
```

The admin key is configured at runtime and is not stored in `appsettings.json`.

A new Basil installation has no admin key configured and therefore starts in **bypass mode**. In bypass mode, admin-protected endpoints do not require authentication.

> [!IMPORTANT]
> Do not expose a new Basil installation to untrusted users while it is in bypass mode. Set an admin key before making the server publicly accessible.

For information about setting and managing the admin key, see [`configuration.md`](../../for-technicians/configuration.md).

---

## Response format

JSON responses from the `api.` host normally use Basil's standard response envelope.

For example:

```json
{
	"success": true,
	"code": 200,
	"message": "Retrieval successful",
	"data": { "...": "the actual response payload" },
	"meta": null,
	"errors": null,
	"timestamp": "2026-08-03T12:00:00Z"
}
```

The exact schema depends on the endpoint.

File downloads and SSE streams **do not** use this envelope.

See [`response-envelope.md`](response-envelope.md).

---

## Live match updates

Match resources can expose live updates through Server-Sent Events (SSE).

SSE connections are long-lived HTTP connections and therefore do not return a normal JSON response envelope.

See [`sse.md`](sse.md) for:

* establishing an SSE connection;
* event formats;
* connection behaviour;
* reconnection handling.

---

## Related resources

* [`getting-started.md`](../bancho/getting-started.md): connecting an osu! client to Basil
* [`response-envelope.md`](response-envelope.md): JSON response format
* [`sse.md`](sse.md): live match updates
* [`irc-client.md`](../irc-client.md): connecting an IRC client
* [`basil-bot.md`](../basil-bot.md): BasilBot chat commands
* [`multiplayer.md`](../../for-developers/multiplayer.md): match implementation and behaviour
* [`configuration.md`](../../for-technicians/configuration.md): admin key and server configuration
