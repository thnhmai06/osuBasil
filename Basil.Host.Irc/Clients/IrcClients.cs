using System.Collections.Concurrent;
using Basil.Application.Sessions.Objects;

namespace Basil.Host.Irc.Clients;

internal sealed class IrcClients
{
	private readonly Lock _gate = new();
	private readonly Dictionary<Session, List<Action<IrcSocket>>> _held = [];
	private readonly ConcurrentDictionary<Session, IrcSocket> _sockets = new();

	public IEnumerable<IrcSocket> All => _sockets.Select(pair => pair.Value);

	public void Bind(IrcSession session, IrcSocket socket)
	{
		lock (_gate)
		{
			_sockets[session] = socket;
			if (_held.Remove(session, out var writes))
				foreach (var write in writes)
					write(socket);
		}
	}

	public void Remove(IrcSocket socket)
	{
		if (socket.Session is not { } session) return;

		lock (_gate)
		{
			_sockets.TryRemove(new KeyValuePair<Session, IrcSocket>(session, socket));
			_held.Remove(session);
		}
	}

	public void Forget(Session session)
	{
		lock (_gate)
		{
			_held.Remove(session);
		}
	}

	public IrcSocket? Find(Session session)
	{
		return session is IrcSession ? _sockets.GetValueOrDefault(session) : null;
	}

	// The session opens before its sign-in returns: what reaches it meanwhile waits for its socket.
	public void DeliverTo(Session session, Action<IrcSocket> write)
	{
		if (session is not IrcSession { IsServerOwned: false }) return;

		lock (_gate)
		{
			if (_sockets.TryGetValue(session, out var socket))
			{
				write(socket);
			}
			else if (session.IsOpen)
			{
				if (!_held.TryGetValue(session, out var writes)) _held[session] = writes = [];
				writes.Add(write);
			}
		}
	}

	public IEnumerable<IrcSocket> Of(IEnumerable<Session> sessions)
	{
		return sessions.Select(Find).OfType<IrcSocket>();
	}
}