using System.Net;
using Basil.Domain.Client;
using Basil.Domain.Cryptography;
using Basil.Domain.Shared;
using Basil.Domain.Users;

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

public sealed record SessionData
{
	public SessionData(UserRecord user, SessionType type, DateTimeOffset startedAt, SessionFingerprint fingerprint,
		Sha256 tokenHash, DateTimeOffset? expiresAt, DateTimeOffset lastActiveAt, SessionEnd? end = null)
	{
		ArgumentNullException.ThrowIfNull(user);
		ArgumentNullException.ThrowIfNull(fingerprint);
		if (type is SessionType.Stable or SessionType.StableTourney && fingerprint.Client is null)
			throw new ArgumentException("An osu! session must report its client.", nameof(fingerprint));
		if (lastActiveAt < startedAt)
			throw new ArgumentOutOfRangeException(nameof(lastActiveAt), lastActiveAt,
				"A session cannot be active before it starts.");

		User = user;
		Type = type.ThrowIfUndefined();
		StartedAt = startedAt;
		Fingerprint = fingerprint;
		TokenHash = tokenHash;
		ExpiresAt = expiresAt;
		LastActiveAt = lastActiveAt;
		End = end;
	}

	public UserRecord User { get; init; }

	public SessionType Type { get; init; }

	public DateTimeOffset StartedAt { get; init; }

	public SessionFingerprint Fingerprint { get; init; }

	public Sha256 TokenHash { get; init; }

	public DateTimeOffset? ExpiresAt { get; init; }

	public DateTimeOffset LastActiveAt { get; init; }

	public SessionEnd? End { get; init; }
}

public sealed record SessionFingerprint(IPAddress Ip, OsuClient? Client = null, string? ClientInfo = null);

public sealed record SessionEnd(DateTimeOffset At, SessionEndReason Reason);