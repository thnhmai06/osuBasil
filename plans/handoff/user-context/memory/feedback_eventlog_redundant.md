---
name: feedback-eventlog-redundant
description: "osuBasil's Event/EventSource/EventDispatcher model uses System.Threading.Channels.Channel<T> directly as the event source — never wrap it in a custom EventLog/Lock class, and never poll it with a wait-loop."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 422abc45-3e7f-4576-a247-b7878807b67d
  modified: 2026-09-28T23:42:56.229Z
---

In osuBasil's event model (`Basil.Domain.Events.Event` + `IEventSource<T>`), an `IEventSource<T>` implementation (`Room`, `ChannelSession`, `UserSession`) backs `Events` with a raw `System.Threading.Channels.Channel<T>` and exposes `ChannelReader<T> Events => _channel.Reader`. `Channel<T>` is already thread-safe and already stores the buffered events — do not add a custom `EventLog` class or a `Lock` around it; that is exactly the redundant abstraction the user removed when they deleted `DomainEventLog`/`IHasDomainEvents`.

**Why:** I recreated a `EventLog` class (mirroring the deleted `DomainEventLog`) to fix a compile error, and the user immediately pushed back: "Việc lưu trữ EventLog, Channel<T> của C# đã lo rồi... tôn trọng quyết định tôi đã làm trước đó." Separately, when I proposed draining a source with a `while (reader.TryRead(...))` loop framed as "polling", they corrected that too: `ChannelReader<T>` supports proper async consumption (`ReadAllAsync`/`await foreach`) and it should be used, not a manual refresh-and-wait loop.

**How to apply:**
- Never reintroduce a wrapper storage class for an `IEventSource<T>`'s buffered events — the `Channel<T>` field is the whole implementation.
- When something needs to consume events from the channel, prefer `await foreach (var e in reader.ReadAllAsync(ct))` for continuous/background consumption. A one-shot, non-blocking `while (reader.TryRead(out var e))` drain (not a wait loop — it returns immediately once empty) is acceptable per the user's own written spec (`Event–EventDispatcher–EventSource Model`, §5) for low-latency/synchronous dispatch, but as of 2026-09-29 the user has twice chosen the BackgroundService/`await foreach` continuous-dispatch design over direct/TryRead dispatch when asked to pick, despite their own spec explicitly favoring direct dispatch to avoid unnecessary BackgroundServices — so default to proposing the BackgroundService design for this project unless told otherwise.
- As of 2026-09-29, `IRoomRegistry` and `IPlayerRegistry` have **no concrete implementation** anywhere in the repo (only `IChannelRegistry` does, via `InMemoryChannelRegistry`), so a BackgroundService that discovers new `Room`/`UserSession` instances via a registry hook cannot yet be built or verified — check this hasn't changed before building on it.

See also [[feedback-room-api-shape]] for the companion correction on how Room's mutation methods should be shaped.
