using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;
using Basil.Shared.Utilities;

namespace Basil.Domain.Authentication;

public sealed class Session : IEquatable<Session>
{
	private State _state = null!;

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
		get => Volatile.Read(ref _state).LastActiveAt;
		init
		{
			if (value < StartedAt)
				throw new ArgumentOutOfRangeException(nameof(value), value,
					"A session cannot be active before it starts.");
			_state = new State(value, null);
		}
	}

	public SessionEnded? Ended => Volatile.Read(ref _state).Ended;

	public bool Equals(Session? other)
	{
		return other is not null && Id == other.Id;
	}

	public void Heartbeat(DateTimeOffset at)
	{
		while (true)
		{
			var current = Volatile.Read(ref _state);
			if (current.Ended is not null || at <= current.LastActiveAt) return;
			if (Interlocked.CompareExchange(ref _state, current with { LastActiveAt = at }, current) == current)
				return;
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
		while (true)
		{
			var current = Volatile.Read(ref _state);
			if (current.Ended is not null) return false;
			if (Interlocked.CompareExchange(ref _state, current with { Ended = new SessionEnded(at, reason) },
				    current) == current) return true;
		}
	}

	public override bool Equals(object? obj)
	{
		return obj is Session other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Id.GetHashCode();
	}

	private sealed record State(DateTimeOffset LastActiveAt, SessionEnded? Ended);
}