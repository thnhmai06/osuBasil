using Basil.Application.Sessions.Multiplayer;

namespace Basil.Infrastructure.Sessions;

/// <inheritdoc cref="IMatchLiveEvents" />
/// <remarks>
///     Each event is a plain, nullable C# event; publishing invokes whichever subscribers are
///     currently attached, synchronously in the caller's thread. Raising an event never performs
///     I/O, so a publisher can fire one safely while holding a <see cref="MatchSession.Lock" />, and
///     each subscriber is expected to buffer the payload for its own asynchronous delivery.
/// </remarks>
public sealed class MatchLiveEvents : IMatchLiveEvents
{
	/// <inheritdoc />
	public event Action<int, byte[]>? MainPublished;

	/// <inheritdoc />
	public event Action<int, string, byte[]>? PlayerScorePublished;

	/// <inheritdoc />
	public bool HasPlayerScoreSubscribers => PlayerScorePublished is not null;

	/// <inheritdoc />
	public event Action<int, byte[]>? SettingsPublished;

	/// <inheritdoc />
	public event Action<int, int, byte[]>? SlotPublished;

	/// <inheritdoc />
	public event Action<int, byte[]>? HostPublished;

	/// <inheritdoc />
	public event Action<int, byte[]>? RefsPublished;

	/// <inheritdoc />
	public event Action<int, byte[]>? BansPublished;

	/// <inheritdoc />
	public event Action<int, byte[]>? TimerPublished;

	/// <inheritdoc />
	public event Action<int, byte[]>? SlotsPublished;

	/// <inheritdoc />
	public event Action<int, byte[]>? ChatPublished;

	/// <inheritdoc />
	public void PublishMain(int matchDbId, byte[] payload)
	{
		MainPublished?.Invoke(matchDbId, payload);
	}

	/// <inheritdoc />
	public void PublishPlayer(int matchDbId, string playerName, byte[] payload)
	{
		PlayerScorePublished?.Invoke(matchDbId, playerName, payload);
	}

	/// <inheritdoc />
	public void PublishSettings(int matchDbId, byte[] payload)
	{
		SettingsPublished?.Invoke(matchDbId, payload);
	}

	/// <inheritdoc />
	public void PublishSlot(int matchDbId, int slotIndex, byte[] payload)
	{
		SlotPublished?.Invoke(matchDbId, slotIndex, payload);
	}

	/// <inheritdoc />
	public void PublishHost(int matchDbId, byte[] payload)
	{
		HostPublished?.Invoke(matchDbId, payload);
	}

	/// <inheritdoc />
	public void PublishRefs(int matchDbId, byte[] payload)
	{
		RefsPublished?.Invoke(matchDbId, payload);
	}

	/// <inheritdoc />
	public void PublishBans(int matchDbId, byte[] payload)
	{
		BansPublished?.Invoke(matchDbId, payload);
	}

	/// <inheritdoc />
	public void PublishTimer(int matchDbId, byte[] payload)
	{
		TimerPublished?.Invoke(matchDbId, payload);
	}

	/// <inheritdoc />
	public void PublishSlots(int matchDbId, byte[] payload)
	{
		SlotsPublished?.Invoke(matchDbId, payload);
	}

	/// <inheritdoc />
	public void PublishChat(int matchDbId, byte[] payload)
	{
		ChatPublished?.Invoke(matchDbId, payload);
	}
}