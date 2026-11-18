using System.Net;
using Basil.Domain.Shared;

namespace Basil.Domain.Authentication;

public sealed class ConnectionRecord : IEquatable<ConnectionRecord>
{
	public required long Id
	{
		get;
		init => field = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Connection id must be positive.");
	}

	public required ConnectionData Value { get; init; }

	public bool Equals(ConnectionRecord? other)
	{
		return other is not null && Id == other.Id;
	}

	public override bool Equals(object? obj)
	{
		return obj is ConnectionRecord other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Id.GetHashCode();
	}
}

public sealed record ConnectionData(
	long SessionId,
	IPAddress Ip,
	string? ClientInfo,
	DateTimeOffset OpenedAt,
	DateTimeOffset LastActiveAt,
	DateTimeOffset? ClosedAt = null,
	SessionEndReason? CloseReason = null)
{
	public long SessionId
	{
		get;
		init => field = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Session id must be positive.");
	} = SessionId > 0
		? SessionId
		: throw new ArgumentOutOfRangeException(nameof(SessionId), "Session id must be positive.");

	public SessionEndReason? CloseReason
	{
		get;
		init => field = value?.ThrowIfUndefined();
	} = CloseReason?.ThrowIfUndefined();
}