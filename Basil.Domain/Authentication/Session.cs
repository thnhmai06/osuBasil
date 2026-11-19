namespace Basil.Domain.Authentication;

public sealed class Session(SessionRecord record) : IEquatable<Session>
{
	private readonly Lock _sync = new();

	public SessionRecord Record { get; } = record;

	public void Heartbeat(DateTimeOffset at)
	{
		using var scope = _sync.EnterScope();
		var data = Record.Value;
		if (data.Ended is null && at > data.LastActiveAt) data.LastActiveAt = at;
	}

	public SessionStatus StatusAt(DateTimeOffset now)
	{
		using var scope = _sync.EnterScope();
		var data = Record.Value;
		if (data.Ended is not null) return SessionStatus.Ended;
		var silent = now - data.LastActiveAt;
		if (silent >= data.Type.ExpiresAfter) return SessionStatus.Expired;
		return silent >= SessionRules.ReplaceableAfter ? SessionStatus.Replaceable : SessionStatus.Active;
	}

	public bool End(DateTimeOffset at, SessionEndReason reason)
	{
		using var scope = _sync.EnterScope();
		var data = Record.Value;
		if (data.Ended is not null) return false;
		data.Ended = new SessionEnded(at, reason);
		return true;
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
