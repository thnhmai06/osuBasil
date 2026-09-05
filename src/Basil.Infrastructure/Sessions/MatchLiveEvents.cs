using System.Collections.Concurrent;
using Basil.Application.Sessions.Multiplayer;

namespace Basil.Infrastructure.Sessions;

/// <inheritdoc cref="IMatchLiveEvents" />
/// <remarks>
///     Each stream is backed by a <see cref="PerMatchHub{THandler}" /> keyed by match database id.
///     Publishing never performs I/O, so a publisher can fire one safely while holding a
///     <see cref="MatchSession.Lock" />, and each subscriber is expected to buffer the payload for
///     its own asynchronous delivery.
/// </remarks>
public sealed class MatchLiveEvents : IMatchLiveEvents
{
	private readonly PerMatchHub<Action<byte[]>> _bans = new();
	private readonly PerMatchHub<Action<byte[]>> _chat = new();
	private readonly PerMatchHub<Action<byte[]>> _host = new();
	private readonly PerMatchHub<Action<byte[]>> _main = new();
	private readonly PerMatchHub<Action<string, byte[]>> _playerScore = new();
	private readonly PerMatchHub<Action<byte[]>> _refs = new();
	private readonly PerMatchHub<Action<int, byte[]>> _slot = new();
	private readonly PerMatchHub<Action<byte[]>> _slots = new();
	private readonly PerMatchHub<Action<byte[]>> _settings = new();
	private readonly PerMatchHub<Action<byte[]>> _timer = new();

	/// <inheritdoc />
	public bool HasPlayerScoreSubscribers(int matchDbId)
	{
		return _playerScore.HasSubscribers(matchDbId);
	}

	/// <inheritdoc />
	public IDisposable SubscribeMain(int matchDbId, Action<byte[]> handler)
	{
		return _main.Subscribe(matchDbId, handler);
	}

	/// <inheritdoc />
	public void PublishMain(int matchDbId, byte[] payload)
	{
		foreach (var handler in _main.Snapshot(matchDbId)) handler(payload);
	}

	/// <inheritdoc />
	public IDisposable SubscribePlayerScore(int matchDbId, Action<string, byte[]> handler)
	{
		return _playerScore.Subscribe(matchDbId, handler);
	}

	/// <inheritdoc />
	public void PublishPlayer(int matchDbId, string playerName, byte[] payload)
	{
		foreach (var handler in _playerScore.Snapshot(matchDbId)) handler(playerName, payload);
	}

	/// <inheritdoc />
	public IDisposable SubscribeSettings(int matchDbId, Action<byte[]> handler)
	{
		return _settings.Subscribe(matchDbId, handler);
	}

	/// <inheritdoc />
	public void PublishSettings(int matchDbId, byte[] payload)
	{
		foreach (var handler in _settings.Snapshot(matchDbId)) handler(payload);
	}

	/// <inheritdoc />
	public IDisposable SubscribeSlot(int matchDbId, Action<int, byte[]> handler)
	{
		return _slot.Subscribe(matchDbId, handler);
	}

	/// <inheritdoc />
	public void PublishSlot(int matchDbId, int slotIndex, byte[] payload)
	{
		foreach (var handler in _slot.Snapshot(matchDbId)) handler(slotIndex, payload);
	}

	/// <inheritdoc />
	public IDisposable SubscribeHost(int matchDbId, Action<byte[]> handler)
	{
		return _host.Subscribe(matchDbId, handler);
	}

	/// <inheritdoc />
	public void PublishHost(int matchDbId, byte[] payload)
	{
		foreach (var handler in _host.Snapshot(matchDbId)) handler(payload);
	}

	/// <inheritdoc />
	public IDisposable SubscribeRefs(int matchDbId, Action<byte[]> handler)
	{
		return _refs.Subscribe(matchDbId, handler);
	}

	/// <inheritdoc />
	public void PublishRefs(int matchDbId, byte[] payload)
	{
		foreach (var handler in _refs.Snapshot(matchDbId)) handler(payload);
	}

	/// <inheritdoc />
	public IDisposable SubscribeBans(int matchDbId, Action<byte[]> handler)
	{
		return _bans.Subscribe(matchDbId, handler);
	}

	/// <inheritdoc />
	public void PublishBans(int matchDbId, byte[] payload)
	{
		foreach (var handler in _bans.Snapshot(matchDbId)) handler(payload);
	}

	/// <inheritdoc />
	public IDisposable SubscribeTimer(int matchDbId, Action<byte[]> handler)
	{
		return _timer.Subscribe(matchDbId, handler);
	}

	/// <inheritdoc />
	public void PublishTimer(int matchDbId, byte[] payload)
	{
		foreach (var handler in _timer.Snapshot(matchDbId)) handler(payload);
	}

	/// <inheritdoc />
	public IDisposable SubscribeSlots(int matchDbId, Action<byte[]> handler)
	{
		return _slots.Subscribe(matchDbId, handler);
	}

	/// <inheritdoc />
	public void PublishSlots(int matchDbId, byte[] payload)
	{
		foreach (var handler in _slots.Snapshot(matchDbId)) handler(payload);
	}

	/// <inheritdoc />
	public IDisposable SubscribeChat(int matchDbId, Action<byte[]> handler)
	{
		return _chat.Subscribe(matchDbId, handler);
	}

	/// <inheritdoc />
	public void PublishChat(int matchDbId, byte[] payload)
	{
		foreach (var handler in _chat.Snapshot(matchDbId)) handler(payload);
	}

	/// <inheritdoc />
	/// <remarks>
	///     Safe to call unconditionally after teardown: by that point every subscriber for this match
	///     has already been completed and deregistered through
	///     <see cref="Basil.Application.Services.SseSubscriberRegistry" />, and nothing can newly
	///     subscribe to a match no longer resolvable from the match registry — a route can only reach
	///     a <c>Subscribe*</c> call by first resolving the live <c>MatchSession</c>.
	/// </remarks>
	public void Forget(int matchDbId)
	{
		_main.Forget(matchDbId);
		_playerScore.Forget(matchDbId);
		_settings.Forget(matchDbId);
		_slot.Forget(matchDbId);
		_host.Forget(matchDbId);
		_refs.Forget(matchDbId);
		_bans.Forget(matchDbId);
		_timer.Forget(matchDbId);
		_slots.Forget(matchDbId);
		_chat.Forget(matchDbId);
	}

	/// <summary>Per-match, per-stream subscriber list for one <typeparamref name="THandler" /> delegate shape.</summary>
	/// <remarks>
	///     Mutation (<see cref="Subscribe" />/deregistration) is synchronized per match via a lock on
	///     that match's own list; <see cref="Snapshot" /> copies the list under the same lock so a
	///     publish never holds a lock while invoking handlers.
	/// </remarks>
	private sealed class PerMatchHub<THandler> where THandler : Delegate
	{
		private readonly ConcurrentDictionary<int, List<THandler>> _byMatch = new();

		public IDisposable Subscribe(int matchDbId, THandler handler)
		{
			var list = _byMatch.GetOrAdd(matchDbId, static _ => []);
			lock (list)
			{
				list.Add(handler);
			}

			return new Subscription(this, matchDbId, handler);
		}

		public THandler[] Snapshot(int matchDbId)
		{
			if (!_byMatch.TryGetValue(matchDbId, out var list)) return [];
			lock (list)
			{
				return [.. list];
			}
		}

		public bool HasSubscribers(int matchDbId)
		{
			return _byMatch.TryGetValue(matchDbId, out var list) && list.Count > 0;
		}

		/// <summary>
		///     Drops this match's list entirely. Only safe once nothing can subscribe to
		///     <paramref name="matchDbId" /> anymore — see <see cref="MatchLiveEvents.Forget" />.
		/// </summary>
		public void Forget(int matchDbId)
		{
			_byMatch.TryRemove(matchDbId, out _);
		}

		private void Unsubscribe(int matchDbId, THandler handler)
		{
			if (!_byMatch.TryGetValue(matchDbId, out var list)) return;
			lock (list)
			{
				list.Remove(handler);
			}
		}

		private sealed class Subscription(PerMatchHub<THandler> hub, int matchDbId, THandler handler) : IDisposable
		{
			public void Dispose()
			{
				hub.Unsubscribe(matchDbId, handler);
			}
		}
	}
}