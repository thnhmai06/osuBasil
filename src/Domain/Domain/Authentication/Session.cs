using System.Runtime.CompilerServices;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;
using Basil.Shared.Utilities;

namespace Basil.Domain.Authentication;

public sealed class Session : IEquatable<Session>
{
	// Swapped whole / compared by ticks: Heartbeat, StatusAt and End run on different threads.
	private long _lastActiveTicks;
	private StrongBox<SessionEnded>? _ended;

	public required long Id
	{
		get;
		init => field = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Session id must be positive.");
	}

	public required Sha256Hash TokenHash { get; init; }

	public required SessionType Type
	{
		get;
		init => field = value.ThrowIfInvalid();
	}

	public required UserRecord User { get; init; }

	public required SessionFingerprint Fingerprint
	{
		get;
		init
		{
			if (Type is SessionType.Stable or SessionType.StableTourney && value.Client is null)
				throw new ArgumentException("An osu! session must report its client.", nameof(value));
			field = value;
		}
	}

	public required DateTimeOffset StartedAt { get; init; }

	public required DateTimeOffset LastActiveAt
	{
		get => new(Volatile.Read(ref _lastActiveTicks), TimeSpan.Zero);
		init
		{
			if (value < StartedAt)
				throw new ArgumentOutOfRangeException(nameof(value), value,
					"A session cannot be active before it starts.");
			_lastActiveTicks = value.UtcTicks;
		}
	}

	public SessionEnded? Ended => Volatile.Read(ref _ended)?.Value;

	public void Heartbeat(DateTimeOffset at)
	{
		var ticks = at.UtcTicks;
		var current = Volatile.Read(ref _lastActiveTicks);
		while (ticks > current && Ended is null)
		{
			var seen = Interlocked.CompareExchange(ref _lastActiveTicks, ticks, current);
			if (seen == current) return;
			current = seen;
		}
	}

	public SessionStatus StatusAt(DateTimeOffset now)
	{
		if (Ended is not null) return SessionStatus.Ended;
		var silent = now - LastActiveAt;
		if (silent >= Type.ExpiresAfter) return SessionStatus.Expired;
		return silent >= Type.ReplaceableAfter ? SessionStatus.Replaceable : SessionStatus.Active;
	}

	public bool End(DateTimeOffset at, SessionEndReason reason)
	{
		return Interlocked.CompareExchange(ref _ended, new StrongBox<SessionEnded>(new SessionEnded(at, reason)),
			null) is null;
	}

	public bool Equals(Session? other)
	{
		return other is not null && Id == other.Id;
	}

	public override bool Equals(object? obj)
	{
		return obj is Session other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Id.GetHashCode();
	}
}
