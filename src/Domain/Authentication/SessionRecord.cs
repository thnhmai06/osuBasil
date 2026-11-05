using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;

namespace Basil.Domain.Authentication;

/// <summary>One client session as it exists in the stored history, whether or not the server is running.</summary>
public sealed class SessionRecord : IEquatable<SessionRecord>
{
	/// <summary>Gets the user who logged in.</summary>
	public required UserRecord User { get; init; }

	/// <summary>Gets the public identifier of the session.</summary>
	public required Sha256 TokenHash { get; init; }

	/// <summary>Gets the kind of client that opened the session.</summary>
	public required SessionType Type
	{
		get;
		init
		{
			value.ThrowIfUndefined();
			if (value.IsOsuSession() && Fingerprint.OsuClient is null)
				throw new ArgumentException("An osu! session must report its client.", nameof(Fingerprint.OsuClient));
			field = value;
		}
	}

	public required SessionFingerprint Fingerprint { get; init; }

	/// <summary>Gets the moment the session started.</summary>
	public required DateTimeOffset CreatedAt { get; init; }

	public required DateTimeOffset ExpiresAt { get; set; }

	public DateTimeOffset? LastActive
	{
		get;
		set
		{
			if (value < CreatedAt)
				throw new ArgumentOutOfRangeException(nameof(CreatedAt), value,
					"A session cannot be active before it starts.");
			if (value >= ExpiresAt)
				throw new ArgumentOutOfRangeException(nameof(CreatedAt), value,
					"A session cannot be active after it expires.");
			field = value;
		}
	} = null;

	public SessionEndStatus? EndStatus { get; set; } = null;


	/// <summary>Determines whether another session is the same record.</summary>
	/// <param name="other">The session to compare against, or <see langword="null" />.</param>
	/// <returns><see langword="true" /> if <paramref name="other" /> has the same <see cref="TokenHash" />.</returns>
	public bool Equals(SessionRecord? other)
	{
		return other is not null && TokenHash == other.TokenHash;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is SessionRecord other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return TokenHash.GetHashCode();
	}
}