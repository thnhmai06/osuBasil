using System.Net;
using Basil.Domain.Shared;

namespace Basil.Domain.Authentication;

/// <summary>One time a client was connected to a session, identified by its id.</summary>
public sealed class ConnectionRecord : IEquatable<ConnectionRecord>
{
	/// <summary>Gets the unique identifier of the connection.</summary>
	public required long Id
	{
		get;
		init => field = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Connection id must be positive.");
	}

	/// <summary>Gets the connection data this identity wraps.</summary>
	public required ConnectionData Value { get; init; }

	/// <summary>Determines whether another connection is the same record.</summary>
	/// <param name="other">The connection to compare against, or <see langword="null" />.</param>
	/// <returns><see langword="true" /> if <paramref name="other" /> has the same <see cref="Id" />.</returns>
	public bool Equals(ConnectionRecord? other)
	{
		return other is not null && Id == other.Id;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is ConnectionRecord other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return Id.GetHashCode();
	}
}

/// <summary>When and from where a client was connected to a session, and how the connection ended.</summary>
/// <param name="SessionId">The session the client was connected to.</param>
/// <param name="Ip">The IP address the client connected from.</param>
/// <param name="ClientInfo">Client information reported for the connection, when available.</param>
/// <param name="OpenedAt">The moment the connection opened.</param>
/// <param name="LastActiveAt">The most recent activity recorded for the connection; the opening for one never used.</param>
/// <param name="ClosedAt">The moment the connection closed, or <see langword="null" /> while it remains open.</param>
/// <param name="CloseReason">Why the connection closed, or <see langword="null" /> while it remains open.</param>
public sealed record ConnectionData(
	long SessionId,
	IPAddress Ip,
	string? ClientInfo,
	DateTimeOffset OpenedAt,
	DateTimeOffset LastActiveAt,
	DateTimeOffset? ClosedAt = null,
	SessionEndReason? CloseReason = null)
{
	/// <summary>Gets the session the client was connected to.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The session id is not positive.</exception>
	public long SessionId
	{
		get;
		init => field = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Session id must be positive.");
	} = SessionId > 0 ? SessionId : throw new ArgumentOutOfRangeException(nameof(SessionId), "Session id must be positive.");

	/// <summary>Gets the reason the connection closed, or <see langword="null" /> while it remains open.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The reason is not a defined value.</exception>
	public SessionEndReason? CloseReason
	{
		get;
		init => field = value?.ThrowIfUndefined();
	} = CloseReason?.ThrowIfUndefined();
}
