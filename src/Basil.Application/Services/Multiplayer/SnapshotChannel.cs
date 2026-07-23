using System.Text.Json;
using System.Text.Json.Nodes;

namespace Basil.Application.Services.Multiplayer;

/// <summary>
///     Lock-free "full snapshot, then deltas" state for one live SSE channel. A publisher calls
///     <see cref="Publish" /> on every mutation — it's a plain reference-type field swap (atomic in
///     C#, made explicit here with <see cref="Volatile" />), never a lock, so a burst of SSE
///     reconnects can never contend with it. A fresh subscriber reads <see cref="Latest" /> directly
///     to get the current full state instead of waiting for the next delta — see
///     <c>LiveSseRoutes</c>'s subscribe-drain-read-resume sequence for why that's race-free: the
///     publisher always writes <see cref="Latest" /> before anything it publishes reaches a
///     subscriber's queue, so anything discarded while draining a just-opened subscription is
///     already reflected in the fresh <see cref="Latest" /> read that follows.
/// </summary>
public sealed class SnapshotChannel<T> where T : class
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private T? _latest;

    /// <summary>The most recently published full state, or default if nothing has been published yet.</summary>
    public T? Latest => Volatile.Read(ref _latest);

    /// <summary>
    ///     Computes the RFC 7396 JSON Merge Patch from the previously published state to
    ///     <paramref name="current" />, atomically stores <paramref name="current" /> as the new
    ///     latest snapshot, and returns the patch as UTF-8 JSON bytes to broadcast to
    ///     already-subscribed connections. The very first call (no previous state) has nothing to
    ///     diff against, so it returns <paramref name="current" /> serialized in full — harmless even
    ///     though no subscriber can realistically exist yet for a match's very first state.
    /// </summary>
    public byte[] Publish(T current)
    {
        var previous = Volatile.Read(ref _latest);
        var patch = previous is null
            ? JsonSerializer.SerializeToNode(current, JsonOptions)
            : JsonMergePatch.Diff(previous, current, JsonOptions);
        Volatile.Write(ref _latest, current);
        return JsonSerializer.SerializeToUtf8Bytes((JsonNode?)patch ?? new JsonObject(), JsonOptions);
    }
}
