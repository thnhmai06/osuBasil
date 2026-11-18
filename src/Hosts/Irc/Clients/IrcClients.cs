using System.Collections.Concurrent;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Users;

namespace Basil.Host.Irc.Clients;

internal sealed class IrcClients
{
	private readonly ConcurrentDictionary<string, IrcSocket> _signingIn = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<Session, IrcSocket> _sockets = new();

	public IEnumerable<IrcSocket> All => _sockets.Select(pair => pair.Value);

	public bool Reserve(string nick, IrcSocket socket)
	{
		return _signingIn.TryAdd(UserData.NormalizeName(nick), socket);
	}

	public void Bind(string nick, IrcSession session, IrcSocket socket)
	{
		_sockets[session] = socket;
		Release(nick, socket);
	}

	public void Release(string nick, IrcSocket socket)
	{
		_signingIn.TryRemove(new KeyValuePair<string, IrcSocket>(UserData.NormalizeName(nick), socket));
	}

	public void Remove(IrcSocket socket)
	{
		if (socket.Session is { } session) _sockets.TryRemove(new KeyValuePair<Session, IrcSocket>(session, socket));
	}

	public IrcSocket? Find(Session session)
	{
		if (session is not IrcSession) return null;
		if (_sockets.TryGetValue(session, out var socket)) return socket;
		// The session opens before its login returns, so a socket still signing in owns the open session of its account.
		return session.IsOpen && _signingIn.TryGetValue(session.Record.Value.NormalizedName, out var pending) &&
		       pending.Session is null
			? pending
			: null;
	}

	public IEnumerable<IrcSocket> Of(IEnumerable<Session> sessions)
	{
		return sessions.Select(Find).OfType<IrcSocket>();
	}
}