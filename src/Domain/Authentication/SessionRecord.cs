using System.Net;
using Basil.Domain.Client;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;

namespace Basil.Domain.Authentication;

/// <summary>One client session as it exists in the stored history, whether or not the server is running.</summary>
public sealed class SessionRecord : IEquatable<SessionRecord>
{
	/// <summary>Initializes a session.</summary>
	/// <param name="id">The public identifier of the session.</param>
	/// <param name="user">The user who logged in.</param>
	/// <param name="type">The kind of client that opened the session.</param>
	/// <param name="startedAt">The moment the session started.</param>
	/// <param name="value">The device, credential, activity and end of the session.</param>
	/// <exception cref="ArgumentException">
	///     <paramref name="id" /> is empty, or <paramref name="type" /> is an osu! kind and the device reports no client.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="type" /> is not a defined kind, or the session was active before it started.
	/// </exception>
	public SessionRecord(Guid id, UserRecord user, SessionType type, DateTimeOffset startedAt, SessionData value)
	{
		ArgumentNullException.ThrowIfNull(user);
		ArgumentNullException.ThrowIfNull(value);
		if (id == Guid.Empty) throw new ArgumentException("A session needs an identifier.", nameof(id));
		if (type is SessionType.Stable or SessionType.StableTourney && value.Fingerprint.Client is null)
			throw new ArgumentException("An osu! session must report its client.", nameof(value));
		if (value.LastActiveAt < startedAt)
			throw new ArgumentOutOfRangeException(nameof(value), value.LastActiveAt,
				"A session cannot be active before it starts.");

		Id = id;
		User = user;
		Type = type.ThrowIfUndefined();
		StartedAt = startedAt;
		Value = value;
	}

	/// <summary>Gets the public identifier of the session.</summary>
	public Guid Id { get; }

	/// <summary>Gets the user who logged in.</summary>
	public UserRecord User { get; }

	/// <summary>Gets the kind of client that opened the session.</summary>
	public SessionType Type { get; }

	/// <summary>Gets the moment the session started.</summary>
	public DateTimeOffset StartedAt { get; }

	/// <summary>Gets the device, credential, activity and end of the session.</summary>
	public SessionData Value { get; }

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

/// <summary>The data of a stored session, in the groups it is read and changed by.</summary>
/// <param name="Fingerprint">Where the session came from.</param>
/// <param name="Credential">The token or key that opens the session.</param>
/// <param name="LastActiveAt">The most recent activity recorded for the session; the start for a session never used.</param>
/// <param name="End">How the session ended, or <see langword="null" /> while it remains active.</param>
public sealed record SessionData(
	SessionFingerprint Fingerprint,
	SessionCredential Credential,
	DateTimeOffset LastActiveAt,
	SessionEnd? End = null);

/// <summary>The device a session came from.</summary>
/// <param name="Ip">The IP address the session came from.</param>
/// <param name="Client">The osu! client that logged in, or <see langword="null" /> for a login without an osu! client.</param>
/// <param name="ClientInfo">Client information reported for the session, when available.</param>
public sealed record SessionFingerprint(IPAddress Ip, OsuClient? Client = null, string? ClientInfo = null);

/// <summary>The token or key associated with a session.</summary>
/// <param name="TokenHash">The hash of an API token or IRC key, or <see langword="null" /> for a session without one.</param>
/// <param name="ExpiresAt">When the token expires, or <see langword="null" /> for a non-expiring session.</param>
public sealed record SessionCredential(Sha256? TokenHash = null, DateTimeOffset? ExpiresAt = null);

/// <summary>How a session ended.</summary>
/// <param name="At">The moment the session ended.</param>
/// <param name="Reason">Why the session ended.</param>
public sealed record SessionEnd(DateTimeOffset At, SessionEndReason Reason);