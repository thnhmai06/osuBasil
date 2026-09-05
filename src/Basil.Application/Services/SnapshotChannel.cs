using System.Text.Json;
using Basil.Application.Diagnostics;
using Basil.Application.Formats;
using Basil.Application.Services.Multiplayer;

namespace Basil.Application.Services;

/// <summary>
///     Holds lock-free "full snapshot, then deltas" state for one live event channel.
/// </summary>
/// <remarks>
///     A publisher calls <see cref="Publish" /> on every mutation, with no lock of its own required
///     around the call (ADR-004 4b: the caller builds and publishes after releasing
///     <c>MatchSession.Lock</c>). A fresh subscriber reads <see cref="Latest" /> directly to get the
///     current full state instead of waiting for the next delta, so it never misses a mutation that
///     happened before it subscribed. The publisher always stores <see cref="Latest" /> before
///     anything it publishes reaches a subscriber's queue, so anything discarded while draining a
///     just-opened subscription is already reflected in the fresh <see cref="Latest" /> read that
///     follows. A <see cref="SequenceGate" /> guards against a publish that lost the race to an
///     unlocked caller with a newer sequence — see <see cref="Publish" />.
/// </remarks>
/// <typeparam name="T">The full-state payload type carried by the channel.</typeparam>
public sealed class SnapshotChannel<T> where T : class
{
	private readonly Lock _sync = new();
	private readonly KeyValuePair<string, object?> _streamTag;
	private long _lastAppliedSequence = -1;
	private T? _latest;

	/// <param name="streamName">Identifies this channel for the stale-drop metric, e.g. <c>"main"</c> or <c>"settings"</c>.</param>
	public SnapshotChannel(string streamName)
	{
		_streamTag = new KeyValuePair<string, object?>("stream", streamName);
	}

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
	///     <paramref name="current" /> serialized in full — never <see langword="null" /> on that
	///     basis, even though no subscriber can realistically exist yet for a match's very first
	///     state.
	///     <paramref name="sequence" /> must come from <c>MatchSession.NextStateVersion()</c>,
	///     allocated while the caller held the match's lock. A call whose sequence does not exceed
	///     the last one this channel actually applied is dropped (returns <see langword="null" />,
	///     also incrementing <see cref="BasilMetrics.StalePublishDropped" />) — this happens when an
	///     older mutation's unlocked build-and-publish finishes after a newer one's, and is a normal,
	///     benign race outcome rather than a bug.
	/// </remarks>
	/// <param name="current">The new full state.</param>
	/// <param name="sequence">This publish's state version, from <c>MatchSession.NextStateVersion()</c>.</param>
	/// <returns>
	///     The UTF-8 JSON bytes of the delta patch (or the full snapshot on first publication) to
	///     broadcast, or <see langword="null" /> when nothing changed or this call was superseded.
	/// </returns>
	public byte[]? Publish(T current, long sequence)
	{
		lock (_sync)
		{
			if (sequence <= _lastAppliedSequence)
			{
				BasilMetrics.StalePublishDropped.Add(1, _streamTag);
				return null;
			}

			var jsonOptions = BasilJsonOptions.Instance;
			var previous = _latest;
			var patch = previous is null
				? JsonSerializer.SerializeToNode(current, jsonOptions)
				: JsonMergePatch.Diff(previous, current, jsonOptions);
			_latest = current;
			_lastAppliedSequence = sequence;
			return patch is null ? null : JsonSerializer.SerializeToUtf8Bytes(patch, jsonOptions);
		}
	}
}