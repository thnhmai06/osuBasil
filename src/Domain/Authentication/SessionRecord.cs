using System.Net;
using Basil.Domain.Client;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;

namespace Basil.Domain.Authentication;

/// <summary>One client session as it exists in the stored history, identified by its id.</summary>
public sealed class SessionRecord : IEquatable<SessionRecord>
{
	/// <summary>
	///     Gets the identifier of the session, which the API shows in place of the hash of its token; it marks the session
	///     and opens nothing.
	/// </summary>
	public required long Id
	{
		get;
		init => field = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Session id must be positive.");
	}

	/// <summary>Gets the session data this identity wraps.</summary>
	public required SessionData Value { get; init; }

	/// <summary>Determines whether another session is the same record.</summary>
	/// <param name="other">The session to compare against, or <see langword="null" />.</param>
	/// <returns><see langword="true" /> if <paramref name="other" /> has the same <see cref="Id" />.</returns>
	public bool Equals(SessionRecord? other)
	{
		return other is not null && Id == other.Id;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is SessionRecord other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return Id.GetHashCode();
	}
}

/// <summary>The data of a session, without its identity: who logged in, from where, with what token, and how it ended.</summary>
public sealed record SessionData
{
	/// <summary>Initializes the data of a session.</summary>
	/// <param name="user">The user who logged in.</param>
	/// <param name="type">The kind of client that opened the session.</param>
	/// <param name="startedAt">The moment the session started.</param>
	/// <param name="fingerprint">Where the session came from.</param>
	/// <param name="tokenHash">The hash of the token or key that opens the session; it finds the session and is never shown.</param>
	/// <param name="expiresAt">When the token expires, or <see langword="null" /> for a session that does not expire.</param>
	/// <param name="lastActiveAt">The most recent activity recorded for the session; the start for a session never used.</param>
	/// <param name="end">How the session ended, or <see langword="null" /> while it remains active.</param>
	/// <exception cref="ArgumentException">
	///     <paramref name="type" /> is an osu! kind and the fingerprint reports no client.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="type" /> is not a defined kind, or the session was active before it started.
	/// </exception>
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

	/// <summary>Gets the user who logged in.</summary>
	public UserRecord User { get; init; }

	/// <summary>Gets the kind of client that opened the session.</summary>
	public SessionType Type { get; init; }

	/// <summary>Gets the moment the session started.</summary>
	public DateTimeOffset StartedAt { get; init; }

	/// <summary>Gets where the session came from.</summary>
	public SessionFingerprint Fingerprint { get; init; }

	/// <summary>Gets the hash of the token or key that opens the session; it finds the session and is never shown.</summary>
	public Sha256 TokenHash { get; init; }

	/// <summary>Gets when the token expires, or <see langword="null" /> for a session that does not expire.</summary>
	public DateTimeOffset? ExpiresAt { get; init; }

	/// <summary>Gets the most recent activity recorded for the session; the start for a session never used.</summary>
	public DateTimeOffset LastActiveAt { get; init; }

	/// <summary>Gets how the session ended, or <see langword="null" /> while it remains active.</summary>
	public SessionEnd? End { get; init; }
}

/// <summary>The device a session came from.</summary>
/// <param name="Ip">The IP address the session came from.</param>
/// <param name="Client">The osu! client that logged in, or <see langword="null" /> for a login without an osu! client.</param>
/// <param name="ClientInfo">Client information reported for the session, when available.</param>
public sealed record SessionFingerprint(IPAddress Ip, OsuClient? Client = null, string? ClientInfo = null);

/// <summary>How a session ended.</summary>
/// <param name="At">The moment the session ended.</param>
/// <param name="Reason">Why the session ended.</param>
public sealed record SessionEnd(DateTimeOffset At, SessionEndReason Reason);