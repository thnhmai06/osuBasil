using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;
using Basil.Shared.Utilities;

namespace Basil.Domain.Authentication;

public sealed class SessionRecord : IEquatable<SessionRecord>
{
	public required long Id
	{
		get;
		init => field = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Session id must be positive.");
	}

	public required SessionData Value { get; init; }

	public bool Equals(SessionRecord? other)
	{
		return other is not null && Id == other.Id;
	}

	public override bool Equals(object? obj)
	{
		return obj is SessionRecord other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Id.GetHashCode();
	}
}

public sealed class SessionData
{
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
		get;
		set
		{
			if (value < StartedAt)
				throw new ArgumentOutOfRangeException(nameof(value), value,
					"A session cannot be active before it starts.");
			field = value;
		}
	}

	public SessionEnded? Ended { get; set; }
}
