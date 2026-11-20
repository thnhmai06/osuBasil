using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Sessions;

/// <summary>The rules of one sign-in: its heartbeat, status and end.</summary>
/// <param name="record">The stored data of the session.</param>
/// <remarks>Two sessions are equal when their records are equal.</remarks>
public sealed class Session(SessionRecord record) : IEquatable<Session>
{
	/// <summary>The stored data of the session.</summary>
	public SessionRecord Record { get; } = record;

	/// <summary>The moment the session expires if it stays silent.</summary>
	public DateTimeOffset ExpiresAt => Record.Value.LastActiveAt + Record.Value.Type.ExpiresAfter;

	/// <summary>Starts a session that has just been heard from.</summary>
	public static Session Start(long id, UserRecord user, SessionType type, SessionFingerprint fingerprint,
		Sha256Hash tokenHash, DateTimeOffset now)
	{
		return new Session(new SessionRecord
		{
			Id = id,
			Value = new SessionData
			{
				User = user,
				Type = type,
				StartedAt = now,
				Fingerprint = fingerprint,
				TokenHash = tokenHash,
				LastActiveAt = now
			}
		});
	}

	/// <summary>Records that the session was heard from; a session silent past its expiry is ended as timed out instead.</summary>
	public SessionActionResult Heartbeat(DateTimeOffset at)
	{
		var data = Record.Value;
		if (data.Ended is not null) return SessionActionResult.Ended;
		if (StatusAt(at) is SessionStatus.Expired)
		{
			End(at, SessionEndReason.TimedOut);
			return SessionActionResult.Expired;
		}

		if (at > data.LastActiveAt) data.LastActiveAt = at;
		return SessionActionResult.Ok;
	}

	/// <summary>Gets the status the session has at a moment.</summary>
	/// <param name="now">The moment to evaluate.</param>
	/// <returns><see cref="SessionStatus.Ended" /> once ended; otherwise <see cref="SessionStatus.Expired" /> from its expiry, <see cref="SessionStatus.Replaceable" /> after it has been silent for <see cref="SessionRules.ReplaceableAfter" />, else <see cref="SessionStatus.Active" />.</returns>
	public SessionStatus StatusAt(DateTimeOffset now)
	{
		var data = Record.Value;
		if (data.Ended is not null) return SessionStatus.Ended;
		if (now >= ExpiresAt) return SessionStatus.Expired;
		return now - data.LastActiveAt >= SessionRules.ReplaceableAfter ? SessionStatus.Replaceable : SessionStatus.Active;
	}

	/// <summary>Ends the session.</summary>
	/// <param name="at">The moment the session ended.</param>
	/// <param name="reason">Why the session ended.</param>
	/// <returns><see cref="SessionActionResult.Ok" /> when the session was ended, <see cref="SessionActionResult.Ended" /> when it had already ended and is left as it was.</returns>
	public SessionActionResult End(DateTimeOffset at, SessionEndReason reason)
	{
		var data = Record.Value;
		if (data.Ended is not null) return SessionActionResult.Ended;
		data.Ended = new SessionEnded(at, reason);
		return SessionActionResult.Ok;
	}

	/// <summary>Makes way for a newcomer of the same type: a session still heard from refuses it, a silent one is ended.</summary>
	public SessionActionResult GiveWay(DateTimeOffset now)
	{
		switch (StatusAt(now))
		{
			case SessionStatus.Ended:
				return SessionActionResult.Ended;
			case SessionStatus.Active:
				return SessionActionResult.Active;
			case SessionStatus.Replaceable:
				End(now, SessionEndReason.Replaced);
				return SessionActionResult.Ok;
			default:
				End(now, SessionEndReason.TimedOut);
				return SessionActionResult.Ok;
		}
	}

	public bool Equals(Session? other)
	{
		return other is not null && Record.Equals(other.Record);
	}

	public override bool Equals(object? obj)
	{
		return obj is Session other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Record.GetHashCode();
	}
}
