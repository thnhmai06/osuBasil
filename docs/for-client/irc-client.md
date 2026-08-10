# Connecting an IRC client

Basil provides an IRC gateway that lets standard IRC clients access the same in-game chat available to osu! clients.

You can use common IRC clients such as HexChat, mIRC, Irssi, WeeChat, or Textual.

The IRC gateway supports reading and sending chat messages and interacting with BasilBot.

---

## Connection reference

The complete client-facing IRC reference is generated from Basil's OpenAPI documentation.

It contains:

* connection settings;
* server password requirements;
* supported IRC commands;
* channel behaviour;
* authentication details.

### Running server

Open:

```text id="j2m7xk"
https://api.<domain>/docs/irc-client/
```

### Online documentation

The same reference is available without a running Basil server:

[Basil IRC client documentation](https://thnhmai06.github.io/osuBasil/)

The generated reference is the authoritative source for the current IRC client contract. This page intentionally does not duplicate the connection parameters or command list.

---

## Using IRC

Connect your IRC client to the Basil server using the connection settings specified by the generated documentation.

Once connected, IRC provides access to Basil's chat system alongside the osu! client.

Messages sent through IRC are visible to compatible osu! clients, and messages sent through the game can be received by IRC clients.

BasilBot commands can also be entered through IRC chat.

---

## See also

* [`api/overview.md`](api/overview.md): Basil's HTTP API
* [`bancho/authentication.md`](bancho/authentication.md): osu! client authentication
* [`../for-developers/irc.md`](../for-developers/irc.md): server-side IRC gateway implementation
