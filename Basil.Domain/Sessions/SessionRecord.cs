using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;
using Basil.Shared.Utilities;

namespace Basil.Domain.Sessions;

/// <summary>A stored session: its identifier and its data.</summary>
/// <remarks>Two records are equal when their identifiers are equal.</remarks>
public sealed class SessionRecord : IEquatable<SessionRecord>
{
	/// <summary>The identifier of the session; it names the session without opening it.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
	public required long Id
	{
		get;
		init => field = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Session id must be positive.");
	}

	/// <summary>The data of the session.</summary>
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

/// <summary>The data of a session: who signed in, how, when, and how it ended.</summary>
public sealed class SessionData
{
	/// <summary>The SHA-256 hash of the session's token, used to find the session; the token itself is not held.</summary>
	public required Sha256Hash TokenHash { get; init; }

	/// <summary>The kind of session.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is not a defined type.</exception>
	public required SessionType Type
	{
		get;
		init => field = value.ThrowIfInvalid();
	}

	/// <summary>The account that signed in.</summary>
	public required UserRecord User { get; init; }

	/// <summary>Where the session comes from.</summary>
	/// <exception cref="ArgumentException">The session is an osu! session and the fingerprint reports no client.</exception>
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

	/// <summary>The moment the session started.</summary>
	public required DateTimeOffset StartedAt { get; init; }

	/// <summary>The moment the session was last heard from.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is before <see cref="StartedAt" />.</exception>
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

	/// <summary>How and when the session ended, or <see langword="null" /> while it has not ended.</summary>
	public SessionEnded? Ended { get; set; }
}
