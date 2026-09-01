using System.Text.Json;
using Basil.Application.Formats;
using Basil.Application.Services.Multiplayer;

namespace Basil.Application.Services;

/// <summary>
///     Holds lock-free "full snapshot, then deltas" state for one live event channel.
/// </summary>
/// <remarks>
///     A publisher calls <see cref="Publish" /> on every mutation; there is never a lock, so a burst
///     of reconnections can never contend with it. A fresh subscriber reads <see cref="Latest" />
///     directly to get the current full state instead of waiting for the next delta, so it never
///     misses a mutation that happened before it subscribed. The publisher always stores
///     <see cref="Latest" /> before anything it publishes reaches a subscriber's queue, so anything
///     discarded while draining a just-opened subscription is already reflected in the fresh
///     <see cref="Latest" /> read that follows.
/// </remarks>
/// <typeparam name="T">The full-state payload type carried by the channel.</typeparam>
public sealed class SnapshotChannel<T> where T : class
{
	private T? _latest;

	/// <summary>Gets the most recently published full state, or the default value when nothing has been published yet.</summary>
	/// <value>The latest published snapshot, or <see langword="null" /> before the first publication.</value>
	public T? Latest => Volatile.Read(ref _latest);

	/// <summary>Stores a new snapshot and computes the delta patch from the previous state.</summary>
	/// <remarks>
	///     Computes the RFC 7396 JSON Merge Patch from the previously published state to
	///     <paramref name="current" />, atomically stores <paramref name="current" /> as the new
	///     latest snapshot, and returns the patch as UTF-8 JSON bytes to broadcast to
	///     already-subscribed connections. Returns <see langword="null" /> when nothing in
	///     <paramref name="current" /> actually differs from the previous snapshot, so a caller can
	///     skip publishing a no-op patch instead of emitting an empty <c>{}</c> on every call. The
	///     very first call (no previous state) has nothing to diff against, so it returns
	///     <paramref name="current" /> serialized in full — never <see langword="null" />, even
	///     though no subscriber can realistically exist yet for a match's very first state.
	/// </remarks>
	/// <param name="current">The new full state.</param>
	/// <returns>
	///     The UTF-8 JSON bytes of the delta patch (or the full snapshot on first publication) to
	///     broadcast, or <see langword="null" /> when nothing changed.
	/// </returns>
	public byte[]? Publish(T current)
	{
		var jsonOptions = BasilJsonOptions.Instance;

		var previous = Volatile.Read(ref _latest);
		var patch = previous is null
			? JsonSerializer.SerializeToNode(current, jsonOptions)
			: JsonMergePatch.Diff(previous, current, jsonOptions);
		Volatile.Write(ref _latest, current);
		return patch is null ? null : JsonSerializer.SerializeToUtf8Bytes(patch, jsonOptions);
	}
}