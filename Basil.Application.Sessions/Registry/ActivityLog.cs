using System.Collections.Concurrent;
using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Persistence;

namespace Basil.Application.Sessions.Registry;

/// <summary>Remembers which sessions were heard from since the last write of their activity to the store.</summary>
public sealed class ActivityLog
{
	private readonly ConcurrentDictionary<long, Session> _heard = new();

	internal void Record(Session session)
	{
		_heard[session.State.Record.Id] = session;
	}

	internal void Forget(long id)
	{
		_heard.TryRemove(id, out _);
	}

	internal List<SessionActivity> Take()
	{
		var sessions = new List<SessionActivity>();
		foreach (var (id, _) in _heard)
			if (_heard.TryRemove(id, out var session))
				sessions.Add(new SessionActivity(id, session.Type, session.LastActiveAt));
		return sessions;
	}
}
